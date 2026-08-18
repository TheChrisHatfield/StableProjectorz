using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using spz;

/// <summary>
/// The add-on list had a ScrollRect but no scrollbar was ever built or assigned, so reaching add-ons
/// below the clip edge required the mouse wheel and nothing showed the list continued. Guards that the
/// bar is built with a working handle and actually wired to the ScrollRect.
/// </summary>
public class AddonListScrollbarContractTests {

	static MethodInfo BuildMethod() =>
		typeof(AddonManager_UI).GetMethod("BuildVerticalScrollbar",
			BindingFlags.NonPublic | BindingFlags.Static);

	[Test]
	public void BuildVerticalScrollbar_ProducesUsableHandleAndTrack() {
		MethodInfo build = BuildMethod();
		Assert.That(build, Is.Not.Null, "BuildVerticalScrollbar(Transform) must exist.");

		var parent = new GameObject("ScrollViewStub", typeof(RectTransform));
		try {
			var bar = build.Invoke(null, new object[] { parent.transform }) as Scrollbar;
			Assert.That(bar, Is.Not.Null, "Builder must return a Scrollbar.");
			Assert.That(bar.handleRect, Is.Not.Null,
				"Scrollbar without handleRect cannot be dragged.");
			Assert.That(bar.targetGraphic, Is.Not.Null, "Handle needs a target graphic to tint.");
			Assert.That(bar.direction, Is.EqualTo(Scrollbar.Direction.BottomToTop),
				"Vertical list bar must run bottom-to-top or the handle tracks inverted.");

			var track = bar.GetComponent<Image>();
			Assert.That(track, Is.Not.Null, "Bar needs a track graphic.");
			Assert.That(track.raycastTarget, Is.True, "Track must accept clicks for page-jump.");
			Assert.That(track.color.a, Is.GreaterThan(0.05f), "Invisible track reads as 'no scrollbar'.");

			var handleImg = bar.targetGraphic as Image;
			Assert.That(handleImg, Is.Not.Null);
			Assert.That(handleImg.raycastTarget, Is.True, "Handle must be draggable.");
			Assert.That(handleImg.color.a, Is.GreaterThan(0.05f), "Handle must be visible.");

			// Handle must stretch inside Sliding Area so ScrollRect can size it to content ratio.
			Assert.That(bar.handleRect.parent, Is.Not.Null);
			Assert.That(bar.handleRect.parent.name, Is.EqualTo("Sliding Area"),
				"Unity's ScrollRect expects handle under a Sliding Area rect.");
			Assert.That(bar.transform.parent, Is.EqualTo(parent.transform),
				"Bar must be a sibling of Viewport under ScrollView, not a child of it.");
		} finally {
			Object.DestroyImmediate(parent);
		}
	}

	[Test]
	public void EnsureListScrollbar_WiresScrollRectAndIsIdempotent() {
		var go = new GameObject("AddonManagerStub", typeof(RectTransform));
		try {
			var ui = go.AddComponent<AddonManager_UI>();
			var scrollGo = new GameObject("ScrollView", typeof(RectTransform));
			scrollGo.transform.SetParent(go.transform, false);
			var scroll = scrollGo.AddComponent<ScrollRect>();

			MethodInfo ensure = typeof(AddonManager_UI).GetMethod("EnsureListScrollbar",
				BindingFlags.NonPublic | BindingFlags.Instance);
			Assert.That(ensure, Is.Not.Null, "EnsureListScrollbar(ScrollRect) must exist.");

			var bar = ensure.Invoke(ui, new object[] { scroll }) as Scrollbar;
			Assert.That(bar, Is.Not.Null, "Ensure must build a bar when none exists.");
			Assert.That(scroll.verticalScrollbar, Is.EqualTo(bar),
				"ScrollRect.verticalScrollbar must be assigned or the bar never moves.");
			Assert.That(scroll.verticalScrollbarVisibility,
				Is.EqualTo(ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport),
				"Short lists must not keep a dead gutter.");

			// Re-open must not stack duplicate bars.
			var again = ensure.Invoke(ui, new object[] { scroll }) as Scrollbar;
			Assert.That(again, Is.EqualTo(bar), "Ensure must reuse the existing bar.");
			Assert.That(scrollGo.GetComponentsInChildren<Scrollbar>(true).Length, Is.EqualTo(1),
				"Repeated OpenPanel must not create a second scrollbar.");
		} finally {
			Object.DestroyImmediate(go);
		}
	}

	[Test]
	public void CreatePanel_And_OpenPanel_BothEnsureScrollbar() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("EnsureListScrollbar(scrollView)"),
			"Panel creation must wire the scrollbar.");
		Assert.That(src, Does.Contain("EnsureListScrollbarFromPanel()"),
			"OpenPanel must wire the bar for panels built before it existed.");
		Assert.That(src, Does.Contain("ReferenceEquals(scroll.verticalScrollbar, _listScrollbar)"),
			"Ensure must re-assign ScrollRect.verticalScrollbar if the live bar ref was dropped.");
	}
}
