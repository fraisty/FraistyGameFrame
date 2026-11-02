using System.Collections.Generic;
using FRFrame;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace FRFrame.Tests.UIExtensions
{
    public sealed class UIRuntimeExtensionsEditModeTests
    {
        private readonly List<Object> _created = new List<Object>();

        [Test] public void RedDotHierarchyReparentsAndRebindingReleasesOnlyItsBinding()
        {
            var tree=new UIRedDotTree();tree.SetParent("Learn","Skills");tree.SetParent("Restore","Skills");
            var go=new GameObject("Notification",typeof(RectTransform),typeof(Image),typeof(UIRedDotBinder));_created.Add(go);
            var graphic=go.GetComponent<Image>();go.GetComponent<UIRedDotBinder>().Bind(tree,"Skills",graphic);
            tree.Set("Learn",true);Assert.That(graphic.enabled,Is.True);tree.Set("Restore",true);tree.Set("Learn",false);Assert.That(graphic.enabled,Is.True);
            tree.SetParent("Restore","Other");Assert.That(graphic.enabled,Is.False);Assert.That(tree.IsVisible("Other"),Is.True);
            Assert.Throws<System.ArgumentException>(()=>tree.SetParent("Other","Restore"));
            int notifications=0;
            var subscription=tree.Subscribe("Learn",visible=>notifications++);
            subscription.Dispose();tree.Set("Learn",true);
            Assert.That(notifications,Is.EqualTo(1),"A disposed listener receives no further notifications.");
            Assert.That(graphic.enabled,Is.True);
            go.GetComponent<UIRedDotBinder>().Bind(tree,"Other",graphic);tree.Set("Restore",false);Assert.That(graphic.enabled,Is.False);
            tree.Set("Learn",false);tree.Set("Learn",true);Assert.That(graphic.enabled,Is.False,"Rebind releases the old listener.");
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
            Time.timeScale = 1f;
        }

        [Test]
        public void RichTextParser_MatchesNestedTagsWithTheSameName()
        {
            UIRichTextParseResult result = new UIRichTextParser().Parse("[b]outer[b]inner[/b]tail[/b]", new UIRichTextConfig());

            Assert.That(result.ParsedText, Is.EqualTo("<b>outer<b>inner</b>tail</b>"));
            Assert.That(result.Warnings, Is.Empty);
        }

        [Test]
        public void RichTextParser_GradientContainingLink_PreservesLinkAndWarnsAboutFallback()
        {
            var config = new UIRichTextConfig
            {
                gradientAliases = new List<UIRichTextGradientAlias>
                {
                    new UIRichTextGradientAlias { key = "Gold", fallbackColor = "#112233" }
                }
            };

            UIRichTextParseResult result = new UIRichTextParser().Parse("[gradient:gold][link:item:7]Loot[/link][/gradient]", config);

            Assert.That(result.ParsedText, Does.Contain("<link=\"item:7\">"));
            Assert.That(result.ParsedText, Does.StartWith("<color=#112233FF>"));
            Assert.That(result.Links, Has.Count.EqualTo(1));
            Assert.That(result.Warnings, Has.Some.Contains("TMP 标签"));
        }

        [Test]
        public void RichTextConfig_AliasesIgnoreCase_AndHashTracksConfigurationChanges()
        {
            var config = new UIRichTextConfig
            {
                spriteAliases = new List<UIRichTextSpriteAlias> { new UIRichTextSpriteAlias { key = "Coin", spriteName = "coin" } },
                colorAliases = new List<UIRichTextColorAlias> { new UIRichTextColorAlias { key = "Danger", color = Color.red } },
                gradientAliases = new List<UIRichTextGradientAlias> { new UIRichTextGradientAlias { key = "Gold", fallbackColor = "#ABCDEF" } }
            };

            Assert.That(config.TryGetSpriteAlias("cOiN", out UIRichTextSpriteAlias sprite), Is.True);
            Assert.That(sprite.spriteName, Is.EqualTo("coin"));
            Assert.That(config.TryGetColor("DANGER", out Color color), Is.True);
            Assert.That(color, Is.EqualTo(Color.red));
            Assert.That(config.TryGetGradient("gOlD", out UIRichTextGradientAlias gradient), Is.True);
            Assert.That(gradient.fallbackColor, Is.EqualTo("#ABCDEF"));

            int initialHash = config.ComputeContentHash();
            config.underlineLinks = !config.underlineLinks;
            int behaviorHash = config.ComputeContentHash();
            config.gradientAliases[0].fallbackColor = "#123456";
            int aliasHash = config.ComputeContentHash();

            Assert.That(behaviorHash, Is.Not.EqualTo(initialHash));
            Assert.That(aliasHash, Is.Not.EqualTo(behaviorHash));
        }

        [TestCase(UIRawImageFitMode.Contain, 100f, 50f, 0f, 0f, 1f, 1f)]
        [TestCase(UIRawImageFitMode.Cover, 100f, 100f, 0.25f, 0f, 0.5f, 1f)]
        [TestCase(UIRawImageFitMode.Fill, 100f, 100f, 0f, 0f, 1f, 1f)]
        public void RawImageFitter_FitsWideTextureIntoSquareContainer(
            UIRawImageFitMode mode, float width, float height,
            float uvX, float uvY, float uvWidth, float uvHeight)
        {
            GameObject container = Track(new GameObject("Container", typeof(RectTransform)));
            var containerRect = (RectTransform)container.transform;
            containerRect.sizeDelta = new Vector2(100f, 100f);
            GameObject imageObject = Track(new GameObject("Image", typeof(RectTransform), typeof(RawImage), typeof(UIRawImageFitter)));
            imageObject.transform.SetParent(container.transform, false);
            var texture = Track(new Texture2D(200, 100));
            var fitter = imageObject.GetComponent<UIRawImageFitter>();
            fitter.FitContainer = containerRect;
            fitter.UseUvRect = true;
            fitter.FitMode = mode;
            fitter.SetTexture(texture);

            var imageRect = (RectTransform)imageObject.transform;
            Rect uv = imageObject.GetComponent<RawImage>().uvRect;
            Assert.That(imageRect.rect.width, Is.EqualTo(width).Within(0.001f));
            Assert.That(imageRect.rect.height, Is.EqualTo(height).Within(0.001f));
            Assert.That(uv.x, Is.EqualTo(uvX).Within(0.001f));
            Assert.That(uv.y, Is.EqualTo(uvY).Within(0.001f));
            Assert.That(uv.width, Is.EqualTo(uvWidth).Within(0.001f));
            Assert.That(uv.height, Is.EqualTo(uvHeight).Within(0.001f));
        }

        [Test]
        public void ToggleGroupBinder_RefreshRemovesOldListenersAndDoesNotDuplicateNewOnes()
        {
            GameObject root = Track(new GameObject("ToggleGroup", typeof(RectTransform), typeof(ToggleGroup)));
            root.SetActive(false);
            var first = CreateToggle(root.transform, "First");
            var removed = CreateToggle(root.transform, "Removed");
            var binder = root.AddComponent<UIToggleGroupBinder>();
            binder.allowSwitchOff = true;
            binder.defaultIndex = -1;
            root.SetActive(true);
            binder.RefreshToggles();

            removed.transform.SetParent(null, false);
            var added = CreateToggle(root.transform, "Added");
            binder.RefreshToggles();
            binder.RefreshToggles();

            int callbacks = 0;
            binder.onSelectedIndexChanged.AddListener(_ => callbacks++);
            first.SetIsOnWithoutNotify(false);
            added.SetIsOnWithoutNotify(false);
            removed.SetIsOnWithoutNotify(true);
            removed.onValueChanged.Invoke(true);
            Assert.That(callbacks, Is.Zero, "Removed toggles must not retain binder listeners.");

            removed.SetIsOnWithoutNotify(false);
            added.SetIsOnWithoutNotify(true);
            added.onValueChanged.Invoke(true);
            Assert.That(callbacks, Is.EqualTo(1), "Repeated refreshes must bind exactly one listener.");
        }

        private Toggle CreateToggle(Transform parent, string name)
        {
            GameObject go = Track(new GameObject(name, typeof(RectTransform), typeof(Toggle)));
            go.transform.SetParent(parent, false);
            return go.GetComponent<Toggle>();
        }

        private T Track<T>(T value) where T : Object
        {
            _created.Add(value);
            return value;
        }
    }
}
