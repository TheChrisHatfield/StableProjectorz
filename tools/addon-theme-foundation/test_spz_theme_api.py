import importlib.util
import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]
SDK_PATH = ROOT / "Assets" / "StreamingAssets" / "AddonSystem" / "spz.py"


def load_sdk():
    spec = importlib.util.spec_from_file_location("spz_theme_test_sdk", SDK_PATH)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class FakeClient:
    def __init__(self):
        self.calls = []

    def _send_request(self, method, params):
        self.calls.append((method, params))
        return {"success": True, "theme_id": params.get("theme_id", "stableprojectorz-default")}


class ThemeApiTransportTests(unittest.TestCase):
    def setUp(self):
        self.sdk = load_sdk()
        self.client = FakeClient()
        self.ui = self.sdk.UIAPI(self.client)

    def test_get_theme_uses_theme_rpc(self):
        result = self.ui.get_theme()
        self.assertTrue(result["success"])
        self.assertEqual(self.client.calls, [("spz.ui.get_theme", {})])

    def test_apply_theme_forwards_id_and_token_copy(self):
        tokens = {"accent": "#FF8800", "panel_bg": "#101010E6"}
        result = self.ui.apply_theme("nomad-inspired", tokens)
        tokens["accent"] = "#000000"

        self.assertEqual(result["theme_id"], "nomad-inspired")
        self.assertEqual(self.client.calls[0], (
            "spz.ui.apply_theme",
            {
                "theme_id": "nomad-inspired",
                "tokens": {"accent": "#FF8800", "panel_bg": "#101010E6"},
            },
        ))

    def test_reset_theme_uses_reset_rpc(self):
        self.ui.reset_theme()
        self.assertEqual(self.client.calls, [("spz.ui.reset_theme", {})])


if __name__ == "__main__":
    unittest.main()
