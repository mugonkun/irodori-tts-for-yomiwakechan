"""The first-run model fetch must leave a cache the *upstream* can read.

``ywk_fetch_models`` downloads with ``revision=<commit sha>`` so a repository
cannot move under us.  ``huggingface_hub`` writes ``refs/<revision>`` only when
the revision it was handed is not already a commit hash
(``file_download.py`` ``_cache_commit_hash_for_specific_revision``), so a
sha-pinned fetch leaves no ``refs/`` entry at all.

Every upstream loader then asks for the same files **without** a revision -- i.e.
for ``main``:

* ``snapshot_download(repo_id=repo_id, allow_patterns=...)``  (inference_runtime.py:568)
* ``hf_hub_download(repo_id=location, filename="weights.pth")`` (codec.py:72)
* ``snapshot_download(repo_id="sony/silentcipher")``           (silentcipher/server.py)

With ``HF_HUB_OFFLINE=1`` baked in (design §2) that is a ``LocalEntryNotFoundError``
on a machine whose cache we filled ourselves.  These tests reproduce exactly that
shape and prove ``write_refs_main`` closes it.
"""

from __future__ import annotations

from pathlib import Path

import pytest

import ywk_fetch_models

SHA = "2b28324dc263ed5e6638b3cf3dd94c82ead07b4b"
REPO = "Aratako/Irodori-TTS-v4.1-Small"


def _sha_pinned_cache(root: Path) -> Path:
    """A cache exactly as a ``revision=<sha>`` download leaves it: no refs/."""
    hub = root / "hub"
    repo_dir = hub / ("models--" + REPO.replace("/", "--"))
    (repo_dir / "blobs").mkdir(parents=True)
    snapshot = repo_dir / "snapshots" / SHA
    snapshot.mkdir(parents=True)
    (snapshot / "model.safetensors").write_bytes(b"\0" * 16)
    assert not (repo_dir / "refs").exists()
    return hub


#: ``HF_HUB_OFFLINE`` is read into a module constant when ``huggingface_hub`` is
#: imported, and this process imported it long ago, so setting the variable now
#: would do nothing.  The variable's only effect is to default this argument to
#: true, so passing it explicitly walks the same code path -- and the run in a
#: fresh process with ``HF_HUB_OFFLINE=1`` gives the identical two errors.
OFFLINE = {"local_files_only": True}


def test_upstreams_call_shape_fails_without_refs_main_and_works_with_it(tmp_path):
    from huggingface_hub import hf_hub_download
    from huggingface_hub.errors import LocalEntryNotFoundError

    hub = _sha_pinned_cache(tmp_path)

    # how the upstream calls it: no revision => "main"
    with pytest.raises(LocalEntryNotFoundError):
        hf_hub_download(repo_id=REPO, filename="model.safetensors", cache_dir=str(hub), **OFFLINE)

    # how we called it while downloading: works, and is why nobody noticed
    assert hf_hub_download(
        repo_id=REPO, filename="model.safetensors", revision=SHA, cache_dir=str(hub), **OFFLINE
    )

    assert ywk_fetch_models.write_refs_main(hub, REPO, SHA) is True
    resolved = hf_hub_download(
        repo_id=REPO, filename="model.safetensors", cache_dir=str(hub), **OFFLINE
    )
    assert Path(resolved).is_file()
    assert SHA in resolved


def test_snapshot_download_shape_also_needs_refs_main(tmp_path):
    from huggingface_hub import snapshot_download
    from huggingface_hub.errors import LocalEntryNotFoundError

    hub = _sha_pinned_cache(tmp_path)
    with pytest.raises(LocalEntryNotFoundError):
        snapshot_download(repo_id=REPO, cache_dir=str(hub), **OFFLINE)

    ywk_fetch_models.write_refs_main(hub, REPO, SHA)
    assert Path(snapshot_download(repo_id=REPO, cache_dir=str(hub), **OFFLINE)).is_dir()


def test_write_refs_main_is_idempotent_and_atomic(tmp_path):
    hub = _sha_pinned_cache(tmp_path)
    assert ywk_fetch_models.write_refs_main(hub, REPO, SHA) is True
    assert ywk_fetch_models.write_refs_main(hub, REPO, SHA) is False
    assert ywk_fetch_models.read_refs_main(hub, REPO) == SHA
    path = ywk_fetch_models.refs_main_path(hub, REPO)
    assert path.name == "main" and path.parent.name == "refs"
    assert list(path.parent.iterdir()) == [path]  # no .tmp left behind


def test_check_only_reports_a_missing_refs_main(tmp_path, capsys):
    """A cache from an older build has the files but not the pointer."""
    hub = _sha_pinned_cache(tmp_path)
    ledger = {
        "generated": "test",
        "repos": [
            {
                "repo": REPO,
                "revision": SHA,
                "role": "checkpoint",
                "files": [{"path": "model.safetensors", "size": 16}],
            }
        ],
    }
    rc = ywk_fetch_models.fetch(ledger, ledger["repos"], tmp_path, check_only=True, force=False)
    assert rc == 2
    assert "refs/main" in capsys.readouterr().out


def test_check_only_passes_once_refs_main_is_there(tmp_path):
    hub = _sha_pinned_cache(tmp_path)
    ywk_fetch_models.write_refs_main(hub, REPO, SHA)
    ledger = {
        "generated": "test",
        "repos": [
            {
                "repo": REPO,
                "revision": SHA,
                "role": "checkpoint",
                "files": [{"path": "model.safetensors", "size": 16}],
            }
        ],
    }
    assert ywk_fetch_models.fetch(ledger, ledger["repos"], tmp_path, True, False) == 0
