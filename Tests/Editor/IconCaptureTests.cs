#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DocMe.AvatarDoctor.Editor;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    public class IconCaptureTests
    {
        TestAvatar avatar;
        AvatarDoc menu;
        const string TempParent = "Assets/TempAssets";
        // The creator's own icons folder is never touched by a test, so saving goes to this temporary root instead.
        const string Folder = TempParent + "/AvatarDoctor Icons";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempParent)) AssetDatabase.CreateFolder("Assets", "TempAssets");
            IconCapture.RootFolder = Folder;
            avatar = TestAvatar.Create();
            menu = avatar.AddMenu();
            avatar.Hat.GetComponent<MeshFilter>().sharedMesh = TestAvatar.QuadWithShapes();
            // A piece of a real avatar has a material. Without one the renderer falls back to Unity's error material,
            // which the camera draws but a direct draw has nothing to use.
            avatar.Hat.GetComponent<MeshRenderer>().sharedMaterial = avatar.MaterialA;
            // The body quad sits at the origin over the same square the hat quad covers, so a frame fitted to the hat
            // would show the body in its place. Moving the hat forward puts the body behind the fitted camera.
            avatar.Hat.transform.position = new Vector3(0f, 0f, 5f);
        }

        [TearDown]
        public void TearDown()
        {
            IconCapture.RootFolder = IconCapture.Folder;
            if (avatar != null) avatar.Dispose();
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            if (AssetDatabase.IsValidFolder(TempParent) && AssetDatabase.FindAssets("", new[] { TempParent }).Length == 0) AssetDatabase.DeleteAsset(TempParent);
        }

        static IconCapture.Frame Facing(Transform target)
            => new IconCapture.Frame { Position = target.position + new Vector3(0f, 0f, -3f), Rotation = Quaternion.identity, FieldOfView = 40f };

        [Test]
        public void NewCapturesGoToTheAvatarDocFolder()
        {
            AssetDatabase.CreateFolder(TempParent, "ResolveTest1");
            string parent = TempParent + "/ResolveTest1";
            try
            {
                Assert.That(IconCapture.ResolveFolder(parent), Is.EqualTo(parent + "/AvatarDoc Icons"));
            }
            finally
            {
                AssetDatabase.DeleteAsset(parent);
            }
        }

        [Test]
        public void AnExistingOldIconFolderKeepsBeingUsed()
        {
            AssetDatabase.CreateFolder(TempParent, "ResolveTest2");
            string parent = TempParent + "/ResolveTest2";
            AssetDatabase.CreateFolder(parent, "AvatarDoctor Icons");
            try
            {
                Assert.That(IconCapture.ResolveFolder(parent), Is.EqualTo(parent + "/AvatarDoctor Icons"));
            }
            finally
            {
                AssetDatabase.DeleteAsset(parent);
            }
        }

        [Test]
        public void TargetsOfAToggleAreItsPiecesOnly()
        {
            var toggle = MenuEntry.Create(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
            toggle.objects.Add(new ObjectTarget { target = avatar.Glasses, activeWhenOn = false });
            toggle.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush" });
            menu.entries.Add(toggle);
            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            var targets = IconCapture.TargetsOf(toggle, tree, avatar.Root.transform);

            Assert.That(targets, Is.EquivalentTo(new Renderer[] { avatar.Hat.GetComponent<MeshRenderer>(), avatar.Body }));
        }

        [Test]
        public void HideOnlyTogglesCaptureTheirPieces()
        {
            var toggle = MenuEntry.Create(EntryKind.Toggle, "Hide hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = false });
            menu.entries.Add(toggle);
            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            var targets = IconCapture.TargetsOf(toggle, tree, avatar.Root.transform);

            Assert.That(targets, Is.EquivalentTo(new Renderer[] { avatar.Hat.GetComponent<MeshRenderer>() }));
        }

        [Test]
        public void RenderShowsOnlyTheTargetsAndRestoresTheFlags()
        {
            var hat = avatar.Hat.GetComponent<MeshRenderer>();
            var frame = IconCapture.Fit(Facing(avatar.Hat.transform), IconCapture.BoundsOf(new Renderer[] { hat }));

            var withHat = IconCapture.Render(frame, new Renderer[] { hat }, 64, new Color(0f, 0f, 0f, 0f));
            var without = IconCapture.Render(frame, new Renderer[] { avatar.Body }, 64, new Color(0f, 0f, 0f, 0f));
            try
            {
                Assert.That(withHat.GetPixel(32, 32).a, Is.GreaterThan(0.5f), "the hat fills the centre");
                Assert.That(withHat.GetPixel(0, 0).a, Is.EqualTo(0f).Within(0.01f), "the corner stays transparent");
                Assert.That(without.GetPixel(32, 32).a, Is.EqualTo(0f).Within(0.01f), "a hidden hat leaves the pixel transparent");
                Assert.That(hat.forceRenderingOff, Is.False);
                Assert.That(avatar.Body.forceRenderingOff, Is.False);
                Assert.That(Object.FindObjectsOfType<Camera>().Any(IsCaptureCamera), Is.False, "the capture camera is gone");
            }
            finally
            {
                Object.DestroyImmediate(withHat);
                Object.DestroyImmediate(without);
            }
        }

        [Test]
        public void RenderIgnoresPreviewHooks()
        {
            var hat = avatar.Hat.GetComponent<MeshRenderer>();
            var frame = IconCapture.Fit(Facing(avatar.Hat.transform), IconCapture.BoundsOf(new Renderer[] { hat }));
            // NDMF's preview hooks a camera that has no parent or no target texture, and then hides the proxied
            // originals while revealing their proxies. This listener stands in for that hook: it fires only on a
            // camera NDMF would take, and on the capture camera alone, so the capture is left out of it.
            Camera.CameraCallback listener = cam =>
            {
                if (cam == null || !IsCaptureCamera(cam)) return;
                if (cam.transform.parent != null && cam.targetTexture != null) return;
                avatar.Body.forceRenderingOff = false;
                hat.forceRenderingOff = true;
            };
            Camera.onPreCull += listener;
            Texture2D icon = null;
            try
            {
                icon = IconCapture.Render(frame, new Renderer[] { hat }, 64, new Color(0f, 0f, 0f, 0f));
                Assert.That(icon.GetPixel(32, 32).a, Is.GreaterThan(0.5f), "the preview hook did not take the capture camera, so the hat still fills the centre");
                Assert.That(hat.forceRenderingOff, Is.False, "the target was never switched off");
                Assert.That(avatar.Body.forceRenderingOff, Is.False, "the non-target's flag is back");
            }
            finally
            {
                Camera.onPreCull -= listener;
                if (icon != null) Object.DestroyImmediate(icon);
            }
        }

        [Test]
        public void DisabledChildrenAreNotCaptured()
        {
            var bell = new GameObject("Bell");
            bell.transform.SetParent(avatar.Hat.transform, false);
            bell.AddComponent<MeshFilter>().sharedMesh = TestAvatar.QuadWithShapes();
            var bellRenderer = bell.AddComponent<MeshRenderer>();
            bell.SetActive(false);
            var muted = new GameObject("Muted");
            muted.transform.SetParent(avatar.Hat.transform, false);
            muted.AddComponent<MeshFilter>().sharedMesh = TestAvatar.QuadWithShapes();
            var mutedRenderer = muted.AddComponent<MeshRenderer>();
            mutedRenderer.enabled = false;
            avatar.Hat.SetActive(false);
            var toggle = MenuEntry.Create(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
            menu.entries.Add(toggle);
            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            var targets = IconCapture.TargetsOf(toggle, tree, avatar.Root.transform);

            Assert.That(targets, Does.Contain(avatar.Hat.GetComponent<MeshRenderer>()), "the switched-off piece itself is captured");
            Assert.That(targets, Has.No.Member(bellRenderer), "a child switched off under the piece is not part of it");
            Assert.That(targets, Has.No.Member(mutedRenderer), "a disabled renderer is not part of it");
        }
        [Test]
        public void InactiveTargetsStillRender()
        {
            var hat = avatar.Hat.GetComponent<MeshRenderer>();
            var frame = IconCapture.Fit(Facing(avatar.Hat.transform), IconCapture.BoundsOf(new Renderer[] { hat }));
            avatar.Hat.SetActive(false);
            Texture2D icon = null;
            try
            {
                icon = IconCapture.Render(frame, new Renderer[] { hat }, 64, new Color(0f, 0f, 0f, 0f));
                Assert.That(icon.GetPixel(32, 32).a, Is.GreaterThan(0.5f), "a piece switched off in the scene is drawn anyway");
                Assert.That(avatar.Hat.activeSelf, Is.False, "the capture left the scene as it found it");
            }
            finally { if (icon != null) Object.DestroyImmediate(icon); }
        }

        /// <summary>A world-space canvas on the UI layer, one unit in front of the frame's camera and big enough to cover the whole view, drawn by its own CanvasRenderer: what a scene with world-space labels (Lightbox Viewer's) has next to an avatar.</summary>
        static GameObject WorldCanvasInFrontOf(IconCapture.Frame frame, out Mesh mesh)
        {
            var go = new GameObject("World canvas", typeof(Canvas), typeof(CanvasRenderer)) { layer = LayerMask.NameToLayer("UI") };
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            go.transform.SetPositionAndRotation(frame.Position + frame.Rotation * Vector3.forward, frame.Rotation);
            go.transform.localScale = Vector3.one * 0.01f;
            mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-500f, -500f, 0f), new Vector3(500f, -500f, 0f), new Vector3(-500f, 500f, 0f), new Vector3(500f, 500f, 0f) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.colors = new[] { Color.red, Color.red, Color.red, Color.red };
            var canvasRenderer = go.GetComponent<CanvasRenderer>();
            canvasRenderer.SetMesh(mesh);
            canvasRenderer.SetMaterial(Canvas.GetDefaultCanvasMaterial(), null);
            return go;
        }

        [Test]
        public void AWorldSpaceCanvasInTheSceneIsNotDrawnIntoACapture()
        {
            var hat = avatar.Hat.GetComponent<MeshRenderer>();
            var frame = IconCapture.Fit(Facing(avatar.Hat.transform), IconCapture.BoundsOf(new Renderer[] { hat }));
            var transparent = new Color(0f, 0f, 0f, 0f);
            var clean = IconCapture.Render(frame, new Renderer[] { hat }, 64, transparent);
            var canvas = WorldCanvasInFrontOf(frame, out var canvasMesh);
            Texture2D withCanvas = null;
            try
            {
                withCanvas = IconCapture.Render(frame, new Renderer[] { hat }, 64, transparent);

                Assert.That(OpaqueCount(clean, 0.02f), Is.GreaterThan(0), "the piece itself is drawn");
                Assert.That(OpaqueCount(withCanvas, 0.02f), Is.EqualTo(OpaqueCount(clean, 0.02f)), "nothing of the canvas, which is not a target, reaches the icon");
                Assert.That(withCanvas.GetPixel(0, 0).a, Is.EqualTo(0f).Within(0.01f), "the corner stays transparent");
            }
            finally
            {
                Object.DestroyImmediate(clean);
                if (withCanvas != null) Object.DestroyImmediate(withCanvas);
                Object.DestroyImmediate(canvas);
                Object.DestroyImmediate(canvasMesh);
            }
        }

        [Test]
        public void ATargetOnAnotherLayerIsStillDrawn()
        {
            var hat = avatar.Hat.GetComponent<MeshRenderer>();
            var frame = IconCapture.Fit(Facing(avatar.Hat.transform), IconCapture.BoundsOf(new Renderer[] { hat }));
            avatar.Hat.layer = 9;
            Texture2D icon = null;
            try
            {
                icon = IconCapture.Render(frame, new Renderer[] { hat }, 64, new Color(0f, 0f, 0f, 0f));

                Assert.That(icon.GetPixel(32, 32).a, Is.GreaterThan(0.5f), "the capture looks at the layers of what it draws, not at one fixed layer");
            }
            finally { if (icon != null) Object.DestroyImmediate(icon); }
        }

        [Test]
        public void TargetsOnDifferentLayersAreAllDrawn()
        {
            var hat = avatar.Hat.GetComponent<MeshRenderer>();
            avatar.Glasses.AddComponent<MeshFilter>().sharedMesh = TestAvatar.QuadWithShapes();
            var glasses = avatar.Glasses.AddComponent<MeshRenderer>();
            glasses.sharedMaterial = avatar.MaterialA;
            avatar.Glasses.transform.position = new Vector3(1.5f, 0f, 5f);
            avatar.Hat.layer = 9;
            avatar.Glasses.layer = 10;
            var both = new Renderer[] { hat, glasses };
            var frame = IconCapture.Fit(Facing(avatar.Hat.transform), IconCapture.BoundsOf(both));
            Texture2D icon = null;
            Texture2D hatOnly = null;
            try
            {
                icon = IconCapture.Render(frame, both, 64, new Color(0f, 0f, 0f, 0f));
                hatOnly = IconCapture.Render(frame, new Renderer[] { hat }, 64, new Color(0f, 0f, 0f, 0f));

                Assert.That(OpaqueCount(icon, 0.02f), Is.GreaterThan(OpaqueCount(hatOnly, 0.02f)), "the second piece, on its own layer, is drawn beside the first");
            }
            finally
            {
                if (icon != null) Object.DestroyImmediate(icon);
                if (hatOnly != null) Object.DestroyImmediate(hatOnly);
            }
        }

        [Test]
        public void TwoEntriesWithTheSameNameGetIconsOfTheirOwn()
        {
            avatar.Glasses.AddComponent<MeshFilter>().sharedMesh = TestAvatar.QuadWithShapes();
            avatar.Glasses.AddComponent<MeshRenderer>().sharedMaterial = avatar.MaterialA;
            avatar.Glasses.transform.position = new Vector3(0f, 0f, 5f);
            var first = MenuEntry.Create(EntryKind.Toggle, "Hat");
            first.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
            var second = MenuEntry.Create(EntryKind.Toggle, "Hat");
            second.objects.Add(new ObjectTarget { target = avatar.Glasses, activeWhenOn = true });
            menu.entries.Add(first);
            menu.entries.Add(second);
            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            int made = IconBatch.CaptureMissing(menu, tree, avatar.Root.transform, new[] { first, second });

            Assert.That(made, Is.EqualTo(2));
            string firstPath = AssetDatabase.GetAssetPath(first.icon);
            string secondPath = AssetDatabase.GetAssetPath(second.icon);
            Assert.That(secondPath, Is.Not.EqualTo(firstPath), "the second Hat must not replace the first one's picture");
            Assert.That(File.Exists(firstPath) && File.Exists(secondPath), Is.True);
        }

        static Texture2D Plain(Color32 colour)
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            texture.SetPixels32(Enumerable.Repeat(colour, 64).ToArray());
            return texture;
        }

        [Test]
        public void ARecaptureOverwritesTheEntrysOwnIconAndNoOtherEntrys()
        {
            var red = Plain(new Color32(255, 0, 0, 255));
            var blue = Plain(new Color32(0, 0, 255, 255));
            try
            {
                var first = menu.AddEntry(EntryKind.Toggle, "Hat");
                IconCapture.Assign(menu, first, IconCapture.Save(red, avatar.Root.name, first.PlainName, first.icon));
                var other = menu.AddEntry(EntryKind.Toggle, "Hat");
                IconCapture.Assign(menu, other, IconCapture.Save(blue, avatar.Root.name, other.PlainName, other.icon));
                string firstPath = AssetDatabase.GetAssetPath(first.icon);
                string otherPath = AssetDatabase.GetAssetPath(other.icon);
                Assert.That(otherPath, Is.Not.EqualTo(firstPath), "a second entry named Hat gets a file of its own");
                var otherBytes = File.ReadAllBytes(otherPath);
                var firstBytes = File.ReadAllBytes(firstPath);

                var again = IconCapture.Save(blue, avatar.Root.name, first.PlainName, first.icon);

                Assert.That(AssetDatabase.GetAssetPath(again), Is.EqualTo(firstPath), "capturing the first entry again replaces its own picture");
                Assert.That(File.ReadAllBytes(firstPath), Is.Not.EqualTo(firstBytes), "with the new capture");
                Assert.That(File.ReadAllBytes(otherPath), Is.EqualTo(otherBytes), "and leaves the other entry's alone");
                Assert.That(Directory.GetFiles(Folder + "/" + avatar.Root.name, "*.png").Length, Is.EqualTo(2), "no third file appears");
            }
            finally
            {
                Object.DestroyImmediate(red);
                Object.DestroyImmediate(blue);
            }
        }

        [Test]
        public void ACopyOfAnEntryThatKeptTheIconNeverOverwritesTheOriginalsPicture()
        {
            var red = Plain(new Color32(255, 0, 0, 255));
            var blue = Plain(new Color32(0, 0, 255, 255));
            try
            {
                var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
                IconCapture.Assign(menu, hat, IconCapture.Save(red, avatar.Root.name, hat.PlainName, hat.icon));
                string hatPath = AssetDatabase.GetAssetPath(hat.icon);
                var hatBytes = File.ReadAllBytes(hatPath);
                // Duplicate clones the entry as it is, so the copy holds the very same icon asset.
                var copy = EntryEditing.Duplicate(menu, hat);
                Assert.That(copy.icon, Is.SameAs(hat.icon), "the copy shares the picture until it gets one of its own");

                var copyIcon = IconCapture.Save(blue, avatar.Root.name, copy.PlainName, copy.icon);

                Assert.That(AssetDatabase.GetAssetPath(copyIcon), Is.Not.EqualTo(hatPath), "the copy's capture goes to a file of its own");
                Assert.That(File.ReadAllBytes(hatPath), Is.EqualTo(hatBytes), "Hat keeps its own picture, which Undo could not bring back once the file is replaced");
                Assert.That(Directory.GetFiles(Folder + "/" + avatar.Root.name, "*.png").Length, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(red);
                Object.DestroyImmediate(blue);
            }
        }

        [Test]
        public void AnIconAlsoUsedByAMenuEditIsNotOverwrittenEither()
        {
            var red = Plain(new Color32(255, 0, 0, 255));
            var blue = Plain(new Color32(0, 0, 255, 255));
            try
            {
                var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
                IconCapture.Assign(menu, hat, IconCapture.Save(red, avatar.Root.name, hat.PlainName, hat.icon));
                string hatPath = AssetDatabase.GetAssetPath(hat.icon);
                var hatBytes = File.ReadAllBytes(hatPath);
                menu.menuEdits.Add(new MenuEdit { id = "reuse", source = new System.Collections.Generic.List<string> { "RootToggle" }, icon = hat.icon });

                var again = IconCapture.Save(blue, avatar.Root.name, hat.PlainName, hat.icon);

                Assert.That(AssetDatabase.GetAssetPath(again), Is.Not.EqualTo(hatPath), "another row uses the same picture, so this capture takes a new file");
                Assert.That(File.ReadAllBytes(hatPath), Is.EqualTo(hatBytes));
            }
            finally
            {
                Object.DestroyImmediate(red);
                Object.DestroyImmediate(blue);
            }
        }

        [Test]
        public void TheCaptureMaskHoldsTheTargetsLayersAndTheLayersOfEnabledLights()
        {
            var hat = avatar.Hat.GetComponent<MeshRenderer>();
            avatar.Hat.layer = 9;
            var lamp = new GameObject("Lamp") { layer = 30 };
            lamp.AddComponent<Light>();
            var switchedOff = new GameObject("Lamp switched off") { layer = 29 };
            switchedOff.AddComponent<Light>().enabled = false;
            var inactive = new GameObject("Lamp on an inactive object") { layer = 28 };
            inactive.AddComponent<Light>();
            inactive.SetActive(false);
            try
            {
                int mask = IconCapture.CaptureMask(new Renderer[] { hat });

                Assert.That(mask & (1 << 9), Is.Not.Zero, "the piece's own layer");
                Assert.That(mask & (1 << 30), Is.Not.Zero, "a light on another layer would otherwise not light the picture");
                Assert.That(mask & (1 << 29), Is.Zero, "a light that is switched off lights nothing");
                Assert.That(mask & (1 << 28), Is.Zero, "and neither does one on an inactive object");
            }
            finally
            {
                Object.DestroyImmediate(lamp);
                Object.DestroyImmediate(switchedOff);
                Object.DestroyImmediate(inactive);
            }
        }

        [Test]
        public void TheCaptureMaskLeavesOutTheUILayerWhileNothingThereIsDrawnOrLit()
        {
            int ui = LayerMask.NameToLayer("UI");
            Assume.That(Object.FindObjectsOfType<Light>().All(l => l.gameObject.layer != ui), "a light of the open scenes sits on the UI layer");
            var hat = avatar.Hat.GetComponent<MeshRenderer>();

            int mask = IconCapture.CaptureMask(new Renderer[] { hat });

            Assert.That(mask & (1 << ui), Is.Zero, "so a world-space canvas on that layer is not drawn");
            Assert.That(mask & 1, Is.Not.Zero, "while the piece's own layer is");
        }

        [Test]
        public void TheCaptureMaskOfNothingIsTheLightsAlone()
        {
            int ui = LayerMask.NameToLayer("UI");
            Assume.That(Object.FindObjectsOfType<Light>().All(l => l.gameObject.layer != ui), "a light of the open scenes sits on the UI layer");

            int mask = IconCapture.CaptureMask(new Renderer[0]);

            Assert.That(mask & (1 << ui), Is.Zero);
        }

        [Test]
        public void AnIconOutsideTheIconsFolderIsNeverOverwritten()
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            const string outside = "Assets/TempAssets/Mine.png";
            try
            {
                File.WriteAllBytes(outside, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(outside, ImportAssetOptions.ForceSynchronousImport);
                var mine = AssetDatabase.LoadAssetAtPath<Texture2D>(outside);
                var before = File.ReadAllBytes(outside);

                var saved = IconCapture.Save(texture, avatar.Root.name, "Hat", mine);

                Assert.That(AssetDatabase.GetAssetPath(saved), Does.StartWith(Folder + "/"), "the capture goes to the icons folder");
                Assert.That(File.ReadAllBytes(outside), Is.EqualTo(before), "the creator's own picture is left as it was");
            }
            finally
            {
                Object.DestroyImmediate(texture);
                AssetDatabase.DeleteAsset(outside);
            }
        }

        /// <summary>True for the capture camera in either shape: a root camera of its own, or the child of the hidden holder.</summary>
        static bool IsCaptureCamera(Camera cam)
        {
            if (cam.name.StartsWith("AvatarDoctor")) return true;
            var parent = cam.transform.parent;
            return parent != null && parent.name.StartsWith("AvatarDoctor");
        }

        [Test]
        public void SaveWritesAnImportedIconAndAssignIsUndoable()
        {
            var toggle = MenuEntry.Create(EntryKind.Toggle, "<b>Hat</b>");
            menu.entries.Add(toggle);
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            try
            {
                var asset = IconCapture.Save(texture, avatar.Root.name, toggle.PlainName);
                string path = AssetDatabase.GetAssetPath(asset);
                Assert.That(path, Is.EqualTo(Folder + "/" + avatar.Root.name + "/Hat.png"));
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer.maxTextureSize, Is.EqualTo(256));
                Assert.That(importer.alphaIsTransparency, Is.True);
                Assert.That(importer.mipmapEnabled, Is.False);

                Undo.IncrementCurrentGroup();
                IconCapture.Assign(menu, toggle, asset);
                Assert.That(toggle.icon, Is.SameAs(asset));
                Undo.PerformUndo();
                Assert.That(menu.entries.Single(e => e.id == toggle.id).icon, Is.Null);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Test]
        public void SaveSanitisesTraversalNames()
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            try
            {
                var asset = IconCapture.Save(texture, "..", "../evil");
                string path = AssetDatabase.GetAssetPath(asset);
                Assert.That(path, Does.StartWith(Folder + "/"));
                Assert.That(path, Does.Not.Contain(".."));
                var written = Directory.GetFiles(Folder, "*.png", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')).ToList();
                Assert.That(written, Is.EqualTo(new[] { path }), "the icon is the only file written, and it is inside the icons folder");

                var centre = avatar.Hat.transform.position;
                var zero = IconCapture.Fit(Facing(avatar.Hat.transform), new Bounds(centre, Vector3.zero));
                Assert.That(Vector3.Distance(zero.Position, centre), Is.GreaterThan(0.01f), "a zero sized target still leaves the camera outside it");
            }
            finally { Object.DestroyImmediate(texture); }
        }

        /// <summary>A frame far enough behind the fit distance that the target looks small and zoomed out in it, the complaint RenderFitted exists to fix.</summary>
        static IconCapture.Frame ZoomedOut(IconCapture.Frame near, Bounds bounds, float times)
        {
            var forward = near.Rotation * Vector3.forward;
            var far = near;
            far.Position = bounds.center - forward * (Vector3.Distance(near.Position, bounds.center) * times);
            return far;
        }

        /// <summary>The width and height, in pixels, of the box around every pixel whose alpha is at or above the threshold. (0, 0) when there is none.</summary>
        static (int width, int height) OpaqueExtent(Texture2D texture, float alphaThreshold)
        {
            var pixels = texture.GetPixels32();
            int size = texture.width;
            byte threshold = (byte)Mathf.RoundToInt(alphaThreshold * 255f);
            int minX = size, minY = texture.height, maxX = -1, maxY = -1;
            for (int y = 0; y < texture.height; y++)
            {
                int row = y * size;
                for (int x = 0; x < size; x++)
                {
                    if (pixels[row + x].a < threshold) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            return maxX < minX ? (0, 0) : (maxX - minX + 1, maxY - minY + 1);
        }

        static int OpaqueCount(Texture2D texture, float alphaThreshold)
        {
            byte threshold = (byte)Mathf.RoundToInt(alphaThreshold * 255f);
            int count = 0;
            var pixels = texture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++) if (pixels[i].a >= threshold) count++;
            return count;
        }

        [Test]
        public void FittedIconFillsTheFrame()
        {
            var hat = avatar.Hat.GetComponent<MeshRenderer>();
            var bounds = IconCapture.BoundsOf(new Renderer[] { hat });
            var near = IconCapture.Fit(Facing(avatar.Hat.transform), bounds);
            var far = ZoomedOut(near, bounds, 6f);

            var icon = IconCapture.RenderFitted(far, new Renderer[] { hat }, 256, new Color(0f, 0f, 0f, 0f));
            try
            {
                var extent = OpaqueExtent(icon, 0.02f);
                float coverage = Mathf.Max(extent.width, extent.height) / (float)icon.width * 100f;
                Assert.That(coverage, Is.GreaterThanOrEqualTo(80f), "a target that looked zoomed out now fills most of the icon");
                Assert.That(coverage, Is.LessThanOrEqualTo(95f), "the margin around the content is kept");
            }
            finally { Object.DestroyImmediate(icon); }
        }

        [Test]
        public void FittedIconIsSquareAndKeepsTheRequestedSize()
        {
            var hat = avatar.Hat.GetComponent<MeshRenderer>();
            var frame = IconCapture.Fit(Facing(avatar.Hat.transform), IconCapture.BoundsOf(new Renderer[] { hat }));

            var icon = IconCapture.RenderFitted(frame, new Renderer[] { hat }, 128, new Color(0f, 0f, 0f, 0f));
            try
            {
                Assert.That(icon.width, Is.EqualTo(128));
                Assert.That(icon.height, Is.EqualTo(128));
            }
            finally { Object.DestroyImmediate(icon); }
        }

        [Test]
        public void FittedIconKeepsTransparency()
        {
            var hat = avatar.Hat.GetComponent<MeshRenderer>();
            var frame = IconCapture.Fit(Facing(avatar.Hat.transform), IconCapture.BoundsOf(new Renderer[] { hat }));
            var solidBackground = new Color(0.2f, 0.4f, 0.8f, 1f);

            var transparentIcon = IconCapture.RenderFitted(frame, new Renderer[] { hat }, 128, new Color(0f, 0f, 0f, 0f));
            var solidIcon = IconCapture.RenderFitted(frame, new Renderer[] { hat }, 128, solidBackground);
            try
            {
                Assert.That(transparentIcon.GetPixel(0, 0).a, Is.EqualTo(0f).Within(0.01f), "a transparent background leaves the corner clear");
                var corner = solidIcon.GetPixel(0, 0);
                Assert.That(corner.a, Is.EqualTo(1f).Within(0.01f), "a solid background fills the corner opaquely");
                Assert.That(corner.r, Is.EqualTo(solidBackground.r).Within(0.05f));
                Assert.That(corner.g, Is.EqualTo(solidBackground.g).Within(0.05f));
                Assert.That(corner.b, Is.EqualTo(solidBackground.b).Within(0.05f));
            }
            finally
            {
                Object.DestroyImmediate(transparentIcon);
                Object.DestroyImmediate(solidIcon);
            }
        }

        [Test]
        public void EmptyRenderIsReturnedUnchanged()
        {
            var frame = Facing(avatar.Hat.transform);
            var background = new Color(0f, 0f, 0f, 0f);
            var targets = new Renderer[0];

            var fitted = IconCapture.RenderFitted(frame, targets, 64, background);
            var plain = IconCapture.Render(frame, targets, 64, background);
            try
            {
                Assert.That(fitted.width, Is.EqualTo(plain.width));
                Assert.That(fitted.GetPixel(32, 32), Is.EqualTo(plain.GetPixel(32, 32)));
                Assert.That(fitted.GetPixel(0, 0).a, Is.EqualTo(0f).Within(0.01f), "nothing was drawn, so the icon is left blank");
            }
            finally
            {
                Object.DestroyImmediate(fitted);
                Object.DestroyImmediate(plain);
            }
        }

        [Test]
        public void ThinDiagonalPieceIsNotClipped()
        {
            var hat = avatar.Hat.GetComponent<MeshRenderer>();
            avatar.Hat.transform.localScale = new Vector3(8f, 0.1f, 1f);
            avatar.Hat.transform.rotation = Quaternion.Euler(0f, 0f, 40f);
            var bounds = IconCapture.BoundsOf(new Renderer[] { hat });
            var near = IconCapture.Fit(Facing(avatar.Hat.transform), bounds);
            var far = ZoomedOut(near, bounds, 8f);
            var background = new Color(0f, 0f, 0f, 0f);

            var working = IconCapture.Render(far, new Renderer[] { hat }, 1024, background);
            var icon = IconCapture.RenderFitted(far, new Renderer[] { hat }, 1024, background);
            try
            {
                var workingExtent = OpaqueExtent(working, 0.02f);
                var iconExtent = OpaqueExtent(icon, 0.02f);
                int workingCount = OpaqueCount(working, 0.02f);
                int iconCount = OpaqueCount(icon, 0.02f);
                float zoom = Mathf.Max(iconExtent.width, iconExtent.height) / (float)Mathf.Max(workingExtent.width, workingExtent.height);
                float expected = workingCount * zoom * zoom;
                Assert.That(iconCount, Is.EqualTo(expected).Within(expected * 0.2f), "no part of the thin diagonal piece is lost to the crop");
            }
            finally
            {
                Object.DestroyImmediate(working);
                Object.DestroyImmediate(icon);
            }
        }

        [Test]
        public void HandFramedShotIsUntouchedWhenFillIsOff()
        {
            var toggle = MenuEntry.Create(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
            menu.entries.Add(toggle);
            var window = ScriptableObject.CreateInstance<IconCaptureWindow>();
            Texture2D expected = null;
            try
            {
                window.Init(menu, toggle.id);
                window.Fill = false;

                var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });
                var targets = IconCapture.TargetsOf(toggle, tree, avatar.Root.transform);
                expected = IconCapture.Render(IconCapture.FromSceneView(), targets, 256, new Color(0f, 0f, 0f, 0f));

                Assert.That(window.Current, Is.Not.Null);
                Assert.That(window.Current.width, Is.EqualTo(expected.width));
                Assert.That(window.Current.GetPixel(128, 128), Is.EqualTo(expected.GetPixel(128, 128)), "the hand framed shot is not cropped");
                Assert.That(window.Current.GetPixel(0, 0), Is.EqualTo(expected.GetPixel(0, 0)));
            }
            finally
            {
                if (expected != null) Object.DestroyImmediate(expected);
                Object.DestroyImmediate(window);
            }
        }

        /// <summary>Capture cameras alive right now, hidden ones included: FindObjectsOfType leaves out an object marked DontSave.</summary>
        static int CaptureCamerasAlive() => Resources.FindObjectsOfTypeAll<Camera>().Count(c => c != null && IsCaptureCamera(c));

        MenuEntry HatToggle()
        {
            var toggle = MenuEntry.Create(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
            menu.entries.Add(toggle);
            return toggle;
        }

        [Test]
        public void TheCaptureWindowKeepsOneCameraWhileItIsOpen()
        {
            var toggle = HatToggle();
            int alive = CaptureCamerasAlive();
            int built = IconCapture.CamerasBuilt;
            var window = ScriptableObject.CreateInstance<IconCaptureWindow>();
            try
            {
                window.Init(menu, toggle.id);
                window.Fill = false;
                window.Fill = true;

                Assert.That(window.Renders, Is.GreaterThanOrEqualTo(3));
                Assert.That(IconCapture.CamerasBuilt - built, Is.EqualTo(1), "every render of the open window goes through one camera");
                Assert.That(CaptureCamerasAlive(), Is.EqualTo(alive + 1), "which stays while the window is open");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
            Assert.That(CaptureCamerasAlive(), Is.EqualTo(alive), "closing the window takes its camera with it");
        }

        [Test]
        public void TheCaptureWindowRendersOnlyWhenTheFrameOrThePlanMoved()
        {
            var toggle = HatToggle();
            double now = 1000.0;
            var frame = IconCapture.Fit(Facing(avatar.Hat.transform), IconCapture.BoundsOf(new Renderer[] { avatar.Hat.GetComponent<MeshRenderer>() }));
            var window = ScriptableObject.CreateInstance<IconCaptureWindow>();
            window.Clock = () => now;
            window.FrameSource = () => frame;
            try
            {
                window.Init(menu, toggle.id);
                int renders = window.Renders;

                now += 1.5;
                window.Poll();
                Assert.That(window.Renders, Is.EqualTo(renders), "nothing moved and nothing changed, so the picture stands");

                frame.Position += Vector3.right * 0.2f;
                now += 0.3;
                window.Poll();
                Assert.That(window.Renders, Is.EqualTo(renders + 1), "the Scene view moved");

                AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
                now += 0.05;
                window.Poll();
                Assert.That(window.Renders, Is.EqualTo(renders + 1), "at most five renders a second");

                now += 0.3;
                window.Poll();
                Assert.That(window.Renders, Is.EqualTo(renders + 2), "the avatar's plan changed");

                now += 0.3;
                window.Poll();
                Assert.That(window.Renders, Is.EqualTo(renders + 2), "and nothing since");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        delegate void Push(ref ObjectChangeEventStream.Builder builder);

        /// <summary>Hands the window one batch of change events, built the way Unity builds the batch it publishes at the end of a frame.</summary>
        static void PublishTo(IconCaptureWindow window, Push push)
        {
            var builder = new ObjectChangeEventStream.Builder(Allocator.Persistent);
            try
            {
                push(ref builder);
                var stream = builder.ToStream(Allocator.Persistent);
                try { window.OnChangesPublished(ref stream); }
                finally { stream.Dispose(); }
            }
            finally
            {
                builder.Dispose();
            }
        }

        static Push AssetChanged(Object asset) => (ref ObjectChangeEventStream.Builder b) =>
        {
            var args = new ChangeAssetObjectPropertiesEventArgs(default(GUID), asset.GetInstanceID(), default(Scene));
            b.PushChangeAssetObjectPropertiesEvent(ref args);
        };

        static Push PropertiesChanged(Object target) => (ref ObjectChangeEventStream.Builder b) =>
        {
            var args = new ChangeGameObjectOrComponentPropertiesEventArgs(target.GetInstanceID(), default(Scene));
            b.PushChangeGameObjectOrComponentPropertiesEvent(ref args);
        };

        /// <summary>Whether this window's own handler is among the ones ObjectChangeEvents calls. Unity keeps them in the private EventWithPerformanceTracker m_ChangesPublishedEvent.</summary>
        static bool Listens(IconCaptureWindow window)
        {
            var field = typeof(ObjectChangeEvents).GetField("m_ChangesPublishedEvent", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, "Unity keeps changesPublished's handlers in m_ChangesPublishedEvent");
            var tracker = field.GetValue(null);
            using (var each = (IEnumerator<ObjectChangeEvents.ObjectChangeEventsHandler>)tracker.GetType().GetMethod("GetEnumerator").Invoke(tracker, null))
                while (each.MoveNext())
                    if (ReferenceEquals(each.Current.Target, window)) return true;
            return false;
        }

        [Test]
        public void TheCaptureWindowRendersAgainWhenItsPiecesChange()
        {
            var toggle = HatToggle();
            double now = 1000.0;
            var frame = IconCapture.Fit(Facing(avatar.Hat.transform), IconCapture.BoundsOf(new Renderer[] { avatar.Hat.GetComponent<MeshRenderer>() }));
            var window = ScriptableObject.CreateInstance<IconCaptureWindow>();
            window.Clock = () => now;
            window.FrameSource = () => frame;
            try
            {
                Assert.That(Listens(window), Is.True, "an open window hears change events");
                window.Init(menu, toggle.id);
                int renders = window.Renders;

                PublishTo(window, AssetChanged(avatar.MaterialA));
                now += 0.3;
                window.Poll();
                Assert.That(window.Renders, Is.EqualTo(renders + 1), "the piece's material was edited in its own inspector");

                now += 0.3;
                window.Poll();
                Assert.That(window.Renders, Is.EqualTo(renders + 1), "once");

                PublishTo(window, AssetChanged(avatar.MaterialB));
                now += 0.3;
                window.Poll();
                Assert.That(window.Renders, Is.EqualTo(renders + 1), "a material the piece does not use");

                PublishTo(window, PropertiesChanged(avatar.Hat.transform));
                now += 0.3;
                window.Poll();
                Assert.That(window.Renders, Is.EqualTo(renders + 2), "the piece was nudged in the scene");

                UiTestEvents.SimulateClick(window.rootVisualElement.Q<Button>("ad-capture-fit"));
                renders = window.Renders;
                frame.Position += Vector3.right * 0.2f;
                now += 0.3;
                window.Poll();
                Assert.That(window.Renders, Is.EqualTo(renders), "a fitted frame holds still while the Scene view moves");

                PublishTo(window, AssetChanged(avatar.MaterialA));
                now += 0.3;
                window.Poll();
                Assert.That(window.Renders, Is.EqualTo(renders + 1), "but follows an edit of the piece");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
            Assert.That(Listens(window), Is.False, "a closed window hears nothing");
        }

        [Test]
        public void TheCaptureRigWorksOutItsMaskForEveryRender()
        {
            var hat = avatar.Hat.GetComponent<MeshRenderer>();
            var frame = IconCapture.Fit(Facing(avatar.Hat.transform), IconCapture.BoundsOf(new Renderer[] { hat }));
            var transparent = new Color(0f, 0f, 0f, 0f);
            Assume.That(Object.FindObjectsOfType<Light>().All(l => l.gameObject.layer != 30 && l.gameObject.layer != 10), "no light of the open scenes sits on layer 10 or 30");
            var lamp = new GameObject("Lamp") { layer = 30 };
            Texture2D first = null, second = null;
            using (var rig = new IconCapture.Rig())
            {
                try
                {
                    avatar.Hat.layer = 9;
                    first = rig.Render(frame, new Renderer[] { hat }, 64, transparent);
                    Assert.That(rig.Camera.cullingMask & (1 << 9), Is.Not.Zero, "the piece's layer");
                    Assert.That(rig.Camera.cullingMask & (1 << 30), Is.Zero, "no light sits on layer 30 yet");

                    avatar.Hat.layer = 10;
                    lamp.AddComponent<Light>();
                    second = rig.Render(frame, new Renderer[] { hat }, 64, transparent);

                    Assert.That(rig.Camera.cullingMask & (1 << 10), Is.Not.Zero, "the piece moved to another layer");
                    Assert.That(rig.Camera.cullingMask & (1 << 30), Is.Not.Zero, "a light switched on since the last render");
                    Assert.That(second.GetPixel(32, 32).a, Is.GreaterThan(0.5f), "and the piece is still drawn");
                    Assert.That(rig.Camera.enabled, Is.False, "the camera draws only when asked, never into the Game view");
                }
                finally
                {
                    if (first != null) Object.DestroyImmediate(first);
                    if (second != null) Object.DestroyImmediate(second);
                    Object.DestroyImmediate(lamp);
                }
            }
        }

        /// <summary>Matches IconBatch's own private frame synthesis, so the test can reproduce the exact frame the batch will pick whether or not a Scene view happens to be open in this Editor session.</summary>
        static IconCapture.Frame SynthesizedFrame(Transform avatarRoot, Bounds bounds)
        {
            var forward = avatarRoot != null ? avatarRoot.forward : Vector3.forward;
            var rotation = Quaternion.LookRotation(-forward, Vector3.up);
            return new IconCapture.Frame { Position = bounds.center + forward, Rotation = rotation, FieldOfView = 30f };
        }

        [Test]
        public void BatchCaptureUsesTheFittedRender()
        {
            // A real Scene view may be open in this live Editor, aimed from either side of this in-memory test
            // avatar's flat quad, so the piece is drawn double sided here: whichever side the batch's own frame
            // selection (Scene view, or a synthesized one) ends up looking from, it is not culled away.
            var doubleSided = new Material(Shader.Find("Sprites/Default"));
            avatar.Hat.GetComponent<MeshRenderer>().sharedMaterial = doubleSided;
            avatar.Hat.transform.localScale = new Vector3(8f, 0.1f, 1f);
            avatar.Hat.transform.rotation = Quaternion.Euler(0f, 0f, 40f);
            var toggle = MenuEntry.Create(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
            menu.entries.Add(toggle);
            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });
            var targets = IconCapture.TargetsOf(toggle, tree, avatar.Root.transform);
            var bounds = IconCapture.BoundsOf(targets);
            // The batch frames from the Scene view when one is active, else a synthesized front view: the test
            // reproduces whichever one it will actually pick so it stays deterministic in this live Editor.
            bool hasSceneView = SceneView.lastActiveSceneView != null && SceneView.lastActiveSceneView.camera != null;
            var baseFrame = hasSceneView ? IconCapture.FromSceneView() : SynthesizedFrame(avatar.Root.transform, bounds);
            var frame = IconCapture.Fit(baseFrame, bounds);
            var expected = IconCapture.RenderFitted(frame, targets, 256, new Color(0f, 0f, 0f, 0f));
            Texture2D readBack = null;
            try
            {
                int made = IconBatch.CaptureMissing(menu, tree, avatar.Root.transform, new[] { toggle });

                Assert.That(made, Is.EqualTo(1));
                Assert.That(toggle.icon, Is.Not.Null);
                var bytes = File.ReadAllBytes(AssetDatabase.GetAssetPath(toggle.icon));
                readBack = new Texture2D(2, 2);
                readBack.LoadImage(bytes);
                var expectedExtent = OpaqueExtent(expected, 0.02f);
                var actualExtent = OpaqueExtent(readBack, 0.02f);
                Assert.That(actualExtent.width, Is.EqualTo(expectedExtent.width).Within(2), "the batch capture used the same fitted render the window preview uses, not the loose renderer bounds");
                Assert.That(actualExtent.height, Is.EqualTo(expectedExtent.height).Within(2));
                Assert.That(Mathf.Max(actualExtent.width, actualExtent.height), Is.GreaterThan(readBack.width / 2), "the piece fills most of the icon instead of looking zoomed out");
            }
            finally
            {
                Object.DestroyImmediate(expected);
                if (readBack != null) Object.DestroyImmediate(readBack);
                Object.DestroyImmediate(doubleSided);
            }
        }
    }
}
#endif
