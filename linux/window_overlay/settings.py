"""User settings persisted to $XDG_CONFIG_HOME/window-overlay/settings.json."""

import json
import os
from dataclasses import asdict, dataclass

DEFAULT_HOTKEY = "<Control><Alt>o"  # GTK accelerator syntax


def _path() -> str:
    base = os.environ.get("XDG_CONFIG_HOME") or os.path.join(os.path.expanduser("~"), ".config")
    return os.path.join(base, "window-overlay", "settings.json")


@dataclass
class Settings:
    toggle_hotkey: str = DEFAULT_HOTKEY  # "" means no hotkey
    snap_to_edges: bool = False

    @classmethod
    def load(cls) -> "Settings":
        try:
            with open(_path(), encoding="utf-8") as f:
                data = json.load(f)
            known = {k: v for k, v in data.items() if k in cls.__dataclass_fields__}
            return cls(**known)
        except (OSError, ValueError, TypeError):
            return cls()  # missing or corrupt: defaults

    def save(self):
        try:
            os.makedirs(os.path.dirname(_path()), exist_ok=True)
            with open(_path(), "w", encoding="utf-8") as f:
                json.dump(asdict(self), f, indent=2)
        except OSError:
            pass  # not being able to save settings shouldn't break the app
