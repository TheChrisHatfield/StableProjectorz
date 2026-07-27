using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ControlNet unit Nomad chrome: flat header/mode cells, field dropdowns, dial tokens, full Restore SPZ unwind.
/// </summary>
public sealed class ControlNetUnitChromeThemeTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ApplyThemeTokensFlattensHeaderModeAndFieldChromeThenRestoreUnwinds() {
		var root = new GameObject("ControlNetUnitChromeTest");
		root.SetActive(false);
		try {
			var unit = root.AddComponent<ControlNetUnit_UI>();

			var headerImgGo = new GameObject("HeaderImg", typeof(RectTransform), typeof(Image));
			headerImgGo.transform.SetParent(root.transform, false);
			var headerImg = headerImgGo.GetComponent<Image>();
			Color authoredHeader = new Color(0.05f, 0.35f, 0.12f, 1f);
			headerImg.color = authoredHeader;

			var headerTmpGo = new GameObject("HeaderTmp", typeof(RectTransform));
			headerTmpGo.transform.SetParent(root.transform, false);
			var headerTmp = headerTmpGo.AddComponent<TextMeshProUGUI>();
			headerTmp.text = "ControlNet 0";
			headerTmp.color = Color.white;

			var contents = new GameObject("Contents", typeof(RectTransform), typeof(Image));
			contents.transform.SetParent(root.transform, false);
			var contentsImg = contents.GetComponent<Image>();
			Color authoredContents = new Color(0.12f, 0.12f, 0.12f, 1f);
			contentsImg.color = authoredContents;

			Toggle MakeModeToggle(string name, bool on, Color authoredFace) {
				var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Toggle));
				go.transform.SetParent(root.transform, false);
				var face = go.GetComponent<Image>();
				face.color = authoredFace;
				var tog = go.GetComponent<Toggle>();
				tog.targetGraphic = face;
				tog.isOn = on;
				var labelGo = new GameObject("Label", typeof(RectTransform));
				labelGo.transform.SetParent(go.transform, false);
				var label = labelGo.AddComponent<TextMeshProUGUI>();
				label.text = name;
				label.color = Color.black;
				return tog;
			}

			var balanced = MakeModeToggle("B", true, new Color(0f, 0.85f, 0.9f, 1f));
			var prompt = MakeModeToggle("P", false, new Color(0.4f, 0.4f, 0.4f, 1f));
			var ctrl = MakeModeToggle("C", false, new Color(0.4f, 0.4f, 0.4f, 1f));
			var low = MakeModeToggle("LOW", true, new Color(0.55f, 0.95f, 0.2f, 1f));

			var ddGo = new GameObject("Dropdown", typeof(RectTransform), typeof(Image), typeof(TMP_Dropdown));
			ddGo.transform.SetParent(contents.transform, false);
			var ddImg = ddGo.GetComponent<Image>();
			Color authoredField = new Color(0.85f, 0.78f, 0.62f, 1f);
			ddImg.color = authoredField;
			var dd = ddGo.GetComponent<TMP_Dropdown>();
			dd.targetGraphic = ddImg;
			var captionGo = new GameObject("Caption", typeof(RectTransform));
			captionGo.transform.SetParent(ddGo.transform, false);
			var caption = captionGo.AddComponent<TextMeshProUGUI>();
			caption.text = "None";
			caption.color = Color.black;
			dd.captionText = caption;

			var dialGo = new GameObject("Dial", typeof(RectTransform));
			dialGo.transform.SetParent(contents.transform, false);
			var dial = dialGo.AddComponent<CircleSlider_Snapping_UI>();
			var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
			fillGo.transform.SetParent(dialGo.transform, false);
			var fill = fillGo.GetComponent<Image>();
			Color authoredFill = new Color(0.1f, 0.7f, 0.2f, 1f);
			fill.color = authoredFill;
			var dialTmpGo = new GameObject("Value", typeof(RectTransform));
			dialTmpGo.transform.SetParent(dialGo.transform, false);
			var dialTmp = dialTmpGo.AddComponent<TextMeshProUGUI>();
			dialTmp.text = "0.3";
			dialTmp.color = Color.white;

			SetPrivate(unit, "_mainHeaderImage", headerImg);
			SetPrivate(unit, "_mainHeader", headerTmp);
			SetPrivate(unit, "_contents", contents);
			SetPrivate(unit, "_balanced_toggle", balanced);
			SetPrivate(unit, "_promptImportant_toggle", prompt);
			SetPrivate(unit, "_ctrlNetImportant_toggle", ctrl);
			SetPrivate(unit, "_lowVRAM_toggle", low);
			SetPrivate(unit, "_controlWeight_slider", dial);
			SetPrivate(dial, "_fillImage", fill);
			SetPrivate(dial, "_text", dialTmp);

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["accent"] = "#F2CA50FF",
					["tab_active"] = "#2A2B30FF",
					["control_bg"] = "#25262AFF",
					["field_bg"] = "#1A1B1FFF",
					["panel_bg"] = "#1E1F23F2",
					["text_primary"] = "#E8E2D6FF",
				},
				"replace",
				out string error), Is.True, error);

			InvokePrivate(unit, "ApplyThemeTokens");

			Assert.That(headerImg.color, Is.EqualTo(SpzUiThemeOps.Active.tabActive));
			Assert.That(contentsImg.color, Is.EqualTo(SpzUiThemeOps.Active.panelBg));
			Assert.That(ddImg.color, Is.EqualTo(SpzUiThemeOps.Active.fieldBg));
			Assert.That(balanced.targetGraphic.color.r, Is.Not.EqualTo(0f).Within(0.01f)); // no cyan plate
			Assert.That(balanced.targetGraphic.color, Is.Not.EqualTo(new Color(0f, 0.85f, 0.9f, 1f)));
			Assert.That(low.targetGraphic.color, Is.Not.EqualTo(new Color(0.55f, 0.95f, 0.2f, 1f)));
			Assert.That(fill.color, Is.Not.EqualTo(authoredFill));

			SpzUiThemeOps.ResetTheme();
			InvokePrivate(unit, "ApplyThemeTokens");

			Assert.That(headerImg.color, Is.EqualTo(authoredHeader));
			Assert.That(contentsImg.color, Is.EqualTo(authoredContents));
			Assert.That(ddImg.color, Is.EqualTo(authoredField));
			Assert.That(balanced.targetGraphic.color, Is.EqualTo(new Color(0f, 0.85f, 0.9f, 1f)));
			Assert.That(low.targetGraphic.color, Is.EqualTo(new Color(0.55f, 0.95f, 0.2f, 1f)));
			Assert.That(fill.color, Is.EqualTo(authoredFill));
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ContextMenuCheckboxKeepsCheckmarkUnderNomad() {
		var root = new GameObject("ControlNetContextCheckTest");
		root.SetActive(false);
		try {
			var unit = root.AddComponent<ControlNetUnit_UI>();

			Toggle MakeMode(string name) {
				var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Toggle));
				go.transform.SetParent(root.transform, false);
				var face = go.GetComponent<Image>();
				var tog = go.GetComponent<Toggle>();
				tog.targetGraphic = face;
				return tog;
			}

			var balanced = MakeMode("B");
			var prompt = MakeMode("P");
			var ctrl = MakeMode("C");
			var low = MakeMode("LOW");

			var menuTogGo = new GameObject("DepthMode", typeof(RectTransform), typeof(Image), typeof(Toggle));
			menuTogGo.transform.SetParent(root.transform, false);
			var menuFace = menuTogGo.GetComponent<Image>();
			var menuTog = menuTogGo.GetComponent<Toggle>();
			menuTog.targetGraphic = menuFace;
			var checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
			checkGo.transform.SetParent(menuTogGo.transform, false);
			var check = checkGo.GetComponent<Image>();
			check.enabled = true;
			var authored = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
			check.sprite = authored;
			menuTog.graphic = check;

			SetPrivate(unit, "_balanced_toggle", balanced);
			SetPrivate(unit, "_promptImportant_toggle", prompt);
			SetPrivate(unit, "_ctrlNetImportant_toggle", ctrl);
			SetPrivate(unit, "_lowVRAM_toggle", low);

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#25262AFF",
					["accent"] = "#F2CA50FF",
					["success"] = "#7BC96FFF",
				},
				"replace",
				out string error), Is.True, error);

			InvokePrivate(unit, "ApplyThemeTokens");

			Assert.That(check.enabled, Is.True, "context-menu Checkmark must stay visible");
			Assert.That(ReferenceEquals(check.sprite, authored), Is.True);
			Assert.That(check.color, Is.EqualTo(SpzUiThemeOps.Active.success));
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	static void SetPrivate(object target, string fieldName, object value) {
		var f = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(f, Is.Not.Null, fieldName);
		f.SetValue(target, value);
	}

	static void InvokePrivate(object target, string methodName) {
		var m = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(m, Is.Not.Null, methodName);
		m.Invoke(target, null);
	}
}
