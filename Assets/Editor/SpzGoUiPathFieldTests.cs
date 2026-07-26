using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;

/// <summary>
/// SPZ GO path fields are resolved by name so demoting Blender.exe cannot break Import/Export.
/// </summary>
public sealed class SpzGoUiPathFieldTests {

	[Test]
	public void FindSpzGoPathField_PrefersImportExportNames_OverSiblingIndex() {
		var method = typeof(AddonUI_MGR).GetMethod(
			"FindSpzGoPathField",
			BindingFlags.Static | BindingFlags.NonPublic);
		Assert.That(method, Is.Not.Null);

		var root = new GameObject("tmp_spz_go_path_fields");
		try {
			var importRow = MakeTextInputRow(root.transform, "TextInput_Import path", "imp.fbx");
			var exportRow = MakeTextInputRow(root.transform, "TextInput_Export path", "exp.fbx");
			var blenderRow = MakeTextInputRow(root.transform, "TextInput_Blender.exe (optional)", "C:/Blender/blender.exe");
			var rows = new List<Transform> { importRow, exportRow, blenderRow };

			var importField = (TMP_InputField)method.Invoke(null, new object[] { rows, true });
			var exportField = (TMP_InputField)method.Invoke(null, new object[] { rows, false });
			Assert.That(importField, Is.Not.Null);
			Assert.That(exportField, Is.Not.Null);
			Assert.That(importField.text, Is.EqualTo("imp.fbx"));
			Assert.That(exportField.text, Is.EqualTo("exp.fbx"));
		} finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void FindSpzGoPathField_LegacyBlenderFirstOrder_StillWorks() {
		var method = typeof(AddonUI_MGR).GetMethod(
			"FindSpzGoPathField",
			BindingFlags.Static | BindingFlags.NonPublic);
		Assert.That(method, Is.Not.Null);

		var root = new GameObject("tmp_spz_go_legacy_fields");
		try {
			var blenderRow = MakeTextInputRow(root.transform, "TextInput_Blender.exe path (auto + editable)", "");
			var importRow = MakeTextInputRow(root.transform, "TextInput_Import: mesh file from Blender → SPZ", "old_import.fbx");
			var exportRow = MakeTextInputRow(root.transform, "TextInput_Export: mesh file from SPZ → disk", "old_export.fbx");
			var rows = new List<Transform> { blenderRow, importRow, exportRow };

			var importField = (TMP_InputField)method.Invoke(null, new object[] { rows, true });
			var exportField = (TMP_InputField)method.Invoke(null, new object[] { rows, false });
			Assert.That(importField.text, Is.EqualTo("old_import.fbx"));
			Assert.That(exportField.text, Is.EqualTo("old_export.fbx"));
		} finally {
			Object.DestroyImmediate(root);
		}
	}

	static Transform MakeTextInputRow(Transform parent, string name, string value) {
		var row = new GameObject(name);
		row.transform.SetParent(parent, false);
		var fieldGo = new GameObject("InputField");
		fieldGo.transform.SetParent(row.transform, false);
		var input = fieldGo.AddComponent<TMP_InputField>();
		var textGo = new GameObject("Text");
		textGo.transform.SetParent(fieldGo.transform, false);
		var tmp = textGo.AddComponent<TextMeshProUGUI>();
		tmp.text = value;
		input.textComponent = tmp;
		input.text = value;
		return row.transform;
	}
}
