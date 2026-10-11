#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDKBase;
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    public class DocsShotTests
    {
        [Test]
        public void MarkersBecomeImagePixels()
        {
            var marker = DocsShot.ToMarker(new Rect(0f, 0f, 360f, 900f), new Rect(40f, 100f, 80f, 20f), 1.5f, 1);

            Assert.That(marker.N, Is.EqualTo(1));
            Assert.That(marker.X, Is.EqualTo(60));
            Assert.That(marker.Y, Is.EqualTo(150));
            Assert.That(marker.W, Is.EqualTo(120));
            Assert.That(marker.H, Is.EqualTo(30));
        }

        [Test]
        public void TheManifestKeepsOtherShots()
        {
            const string existing = "{\"shots\": {"
                + "\"home/hero\": {\"version\": \"0.2.0\", \"taken\": \"2026-09-01\"},"
                + "\"reference-toggle/overview\": {\"version\": \"0.2.0\", \"taken\": \"2026-09-01\"}}}";

            var shots = Shots(DocsShot.MergeManifest(existing, "reference-toggle/overview", "0.3.0", "2026-10-11"));

            Assert.That(shots.Keys, Is.EquivalentTo(new[] { "home/hero", "reference-toggle/overview" }));
            Assert.That(Field(shots, "home/hero", "version"), Is.EqualTo("0.2.0"), "another shot keeps its version");
            Assert.That(Field(shots, "home/hero", "taken"), Is.EqualTo("2026-09-01"), "and its date");
            Assert.That(Field(shots, "reference-toggle/overview", "version"), Is.EqualTo("0.3.0"), "this shot gets the new version");
            Assert.That(Field(shots, "reference-toggle/overview", "taken"), Is.EqualTo("2026-10-11"), "and today's date");

            var first = Shots(DocsShot.MergeManifest(null, "home/hero", "0.3.0", "2026-10-11"));
            Assert.That(first.Keys, Is.EqualTo(new[] { "home/hero" }), "a missing manifest starts with this shot");
        }

        [Test]
        public void AShotOverTheLimitIsReported()
        {
            Assert.That(DocsShot.SizeProblem(260_000), Does.Contain("250 KB"));
            Assert.That(DocsShot.SizeProblem(90_000), Is.Null);
        }

        [Test]
        public void ALightThemeStopsTheRun()
        {
            var avatar = TestAvatar.Create();
            var oldTheme = DocsShot.IsDarkTheme;
            var png = DocsShot.ShotPath("docs-shot-tests", "light-theme");
            var manifest = DocsShot.ManifestPath();
            var manifestBefore = File.Exists(manifest) ? File.ReadAllText(manifest) : null;
            int open = DocsShot.OpenInspectors;
            try
            {
                var menu = avatar.AddMenu();
                var inspector = DocsShot.Inspector(menu);
                Assert.That(DocsShot.OpenInspectors, Is.EqualTo(open + 1), "the inspector's editor is kept until the shot is done");
                DocsShot.IsDarkTheme = () => false;

                var thrown = Assert.Throws<InvalidOperationException>(() => DocsShot.Take("docs-shot-tests", "light-theme", inspector));

                Assert.That(thrown.Message, Is.EqualTo("Docs screenshots are taken in Unity's dark theme. Switch Edit > Preferences > General > Editor Theme to Dark, then run the DocsShots tests again."));
                Assert.That(File.Exists(png), Is.False, "no picture is written");
                Assert.That(File.Exists(Path.ChangeExtension(png, ".json")), Is.False, "no marker file is written");
                Assert.That(Directory.Exists(Path.GetDirectoryName(png)), Is.False, "no folder is made");
                Assert.That(File.Exists(manifest) ? File.ReadAllText(manifest) : null, Is.EqualTo(manifestBefore), "the manifest is left as it was");
                Assert.That(DocsShot.OpenInspectors, Is.EqualTo(open), "the inspector's editor is destroyed even when the run stops");
            }
            finally
            {
                DocsShot.IsDarkTheme = oldTheme;
                avatar.Dispose();
            }
        }

        [Test]
        public void TheCropStartsWhereTheWindowsRootStarts()
        {
            var view = new VisualElement();
            view.Add(new VisualElement { style = { height = 300f } });

            InspectorShots.InHostWindow(view, 360f, 600f, (window, frame) =>
            {
                float rootTop = window.rootVisualElement.worldBound.y;
                var shot = new InspectorShots.ShotFrame(window, frame, (int)window.position.width, (int)window.position.height, inFront: true);

                var area = DocsShot.CropAndMark(shot, view, new (string, int)[0], new List<DocsShot.Marker>());

                Assert.That(rootTop, Is.GreaterThan(0f), "the window's root starts below its tab strip, where the read starts");
                Assert.That(area.y, Is.EqualTo(Mathf.RoundToInt(rootTop)), "the crop starts where the root starts");
            });
        }

        [Test]
        public void CropAndMarkKeepsTheContentAndPlacesTheMarkers()
        {
            var view = new VisualElement();
            view.Add(new VisualElement { style = { height = 40f } });
            view.Add(new VisualElement { name = "docs-marked", style = { height = 20f, width = 100f, marginLeft = 10f } });
            view.Add(new VisualElement { style = { height = 240f } });

            InspectorShots.InHostWindow(view, 360f, 600f, (window, frame) =>
            {
                int rootTop = Mathf.RoundToInt(window.rootVisualElement.worldBound.y);
                var recorded = new List<DocsShot.Marker>();
                var shot = new InspectorShots.ShotFrame(window, frame, (int)window.position.width, (int)window.position.height, inFront: true);

                var area = DocsShot.CropAndMark(shot, view, new[] { ("docs-marked", 1) }, recorded);

                Assert.That(area, Is.EqualTo(new RectInt(0, rootTop, 360, 300)), "the picture keeps the root's rows down to the end of the view's 300 points");
                var marker = recorded.Single();
                Assert.That((marker.N, marker.X, marker.Y, marker.W, marker.H), Is.EqualTo((1, 10, 40, 100, 20)),
                    "the marker sits on the element, counted from the top of the kept picture");
            });
        }

        [Test]
        public void AScaledDisplayIsFlagged()
        {
            Assert.That(DocsShot.ScaledDisplayProblem(1f), Is.Null);
            Assert.That(DocsShot.ScaledDisplayProblem(1.5f), Does.Contain("1.5"));
        }

        [Test]
        public void AListThatDrewOnlySomeRowsStopsTheRun()
        {
            var list = new ListView(new List<string> { "Hand", "Head", "Finger" }, 22f, () => new Label(), (row, index) => ((Label)row).text = "item " + index)
            {
                style = { height = 120f },
            };
            var panel = new VisualElement { name = "docs-list-panel" };
            panel.Add(list);

            LayoutProbe.Measure(panel, () =>
            {
                DocsShot.SettleLists(panel);
                var rows = ShownRows(list);
                Assert.That(rows.Count, Is.EqualTo(3), "all three rows drew");

                rows[2].style.display = DisplayStyle.None;
                LayoutProbe.Validate(panel);

                var thrown = Assert.Throws<InvalidOperationException>(() => DocsShot.CheckRowsDrawn(panel));
                Assert.That(thrown.Message, Does.Contain("docs-list-panel").And.Contain("2 rows"), "a list that fits without scrolling shows every row or stops the run");
            });
        }

        [Test]
        public void TheHeaderAndTheDetailPanelCanBeMarked()
        {
            var avatar = TestAvatar.Create();
            try
            {
                var menu = avatar.AddMenu();
                var view = new AvatarDoctorView(new SerializedObject(menu), menu);

                Assert.That(view.Header.name, Is.EqualTo("ad-header"));
                Assert.That(view.Detail.name, Is.EqualTo("ad-detail"));
                Assert.That(DocsShot.FindMarked(view, "ad-header"), Is.SameAs(view.Header));
                Assert.That(DocsShot.FindMarked(view, "ad-detail"), Is.SameAs(view.Detail));
            }
            finally
            {
                avatar.Dispose();
                AvatarPlanCache.Invalidate();
            }
        }

        [Test]
        public void AnInspectorStartsWithEveryTipSeen()
        {
            var avatar = TestAvatar.Create();
            int open = DocsShot.OpenInspectors;
            try
            {
                var menu = avatar.AddMenu();
                var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
                hat.objects.Add(new ObjectTarget { target = avatar.Hat });
                foreach (var showTips in new[] { false, true })
                {
                    var inspector = DocsShot.Inspector(menu, showTips: showTips);
                    try
                    {
                        LayoutProbe.Measure(inspector, () =>
                        {
                            inspector.Q<AvatarDoctorView>().Tree.SelectEntry(hat.id);
                            DocsShot.SettleLists(inspector);

                            var tips = inspector.Query(className: "ad-tip").ToList().Where(tip => tip.worldBound.height > 0f).Select(tip => tip.name).ToList();
                            if (showTips) Assert.That(tips, Is.SupersetOf(new[] { "ad-tip-header", "ad-tip-toggle" }), "a shot that asks for tips shows them");
                            else Assert.That(tips, Is.Empty, "a shot starts with every first-use tip seen");
                        });
                    }
                    finally
                    {
                        DocsShot.ReleaseInspectors(inspector);
                    }
                }
                Assert.That(DocsShot.OpenInspectors, Is.EqualTo(open));
            }
            finally
            {
                avatar.Dispose();
                AvatarPlanCache.Invalidate();
            }
        }

        [Test]
        public void AWindowThatNeverCameToTheFrontStopsTheRun()
        {
            var recorded = new List<DocsShot.Marker>();
            var notInFront = new InspectorShots.ShotFrame(null, null, 360, 894, inFront: false);

            var thrown = Assert.Throws<InvalidOperationException>(() => DocsShot.CropAndMark(notInFront, new VisualElement(), new[] { ("ad-search", 1) }, recorded));

            Assert.That(thrown.Message, Does.StartWith("The docs screenshot window never came to the front of the screen"));
            Assert.That(recorded, Is.Empty, "nothing is recorded for a picture that may show another window");
        }

        [Test]
        public void ListRowsDrawBeforeTheCapture()
        {
            var avatar = TestAvatar.Create();
            try
            {
                var menu = avatar.AddMenu();
                var entry = menu.AddEntry(EntryKind.Trigger, "Headpat");
                entry.contactTags = new List<string> { "Hand", "Head", "Finger" };
                var serialized = new SerializedObject(menu);
                var tags = serialized.FindProperty(nameof(AvatarDoc.entries)).GetArrayElementAtIndex(0).FindPropertyRelative(nameof(MenuEntry.contactTags));

                // Set up the way the detail panel's lists are: bound to the entry's rows, rows of their own height.
                var list = new ListView
                {
                    virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                    showBoundCollectionSize = false,
                    reorderable = true,
                    reorderMode = ListViewReorderMode.Animated,
                };
                list.BindProperty(tags);
                list.makeItem = () => new Label();
                list.bindItem = (row, index) => ((Label)row).text = tags.GetArrayElementAtIndex(index).stringValue;
                var view = new VisualElement();
                view.Add(list);

                LayoutProbe.Measure(view, () =>
                {
                    DocsShot.SettleLists(view);

                    var rows = list.Query(className: BaseVerticalCollectionView.itemUssClassName).ToList()
                        .Where(row => row.resolvedStyle.display != DisplayStyle.None).ToList();
                    Assert.That(rows.Count, Is.EqualTo(3), "one row per item");
                    Assert.That(rows.Select(row => row as Label ?? row.Q<Label>()).Select(label => label?.text),
                        Is.EqualTo(new[] { "Hand", "Head", "Finger" }), "each row holds its item");
                });
            }
            finally
            {
                avatar.Dispose();
            }
        }

        [Test]
        public void AnInspectorsListsDrawTheirRows()
        {
            var avatar = DocsAvatar.Create();
            int open = DocsShot.OpenInspectors;
            try
            {
                var menu = avatar.AddMenu();
                var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
                hat.objects.Add(new ObjectTarget { target = avatar.Hat });
                hat.objects.Add(new ObjectTarget { target = avatar.Glasses });
                hat.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 0.9f });
                var inspector = DocsShot.Inspector(menu);
                try
                {
                    LayoutProbe.Measure(inspector, () =>
                    {
                        var view = inspector.Q<AvatarDoctorView>();
                        Assert.That(view, Is.Not.Null, "the inspector holds AvatarDoc's view");
                        view.Tree.SelectEntry(hat.id);

                        DocsShot.SettleLists(inspector);

                        var objects = ShownRows(view.Detail.Q<TargetListView>("ad-objects").Q<ListView>());
                        Assert.That(objects.Select(row => row.Q<ObjectField>("target")?.value), Is.EqualTo(new Object[] { avatar.Hat, avatar.Glasses }),
                            "the Objects list draws a row per object");
                        Assert.That(ShownRows(view.Detail.Query<TargetListView>().ToList().First(l => l.name == "ad-properties").Q<ListView>()).Count, Is.EqualTo(1),
                            "and the Properties list its one row");
                    });
                }
                finally
                {
                    DocsShot.ReleaseInspectors(inspector);
                }
                Assert.That(DocsShot.OpenInspectors, Is.EqualTo(open));
            }
            finally
            {
                avatar.Dispose();
            }
        }

        static List<VisualElement> ShownRows(ListView list) => list.Query(className: BaseVerticalCollectionView.itemUssClassName).ToList()
            .Where(row => row.resolvedStyle.display != DisplayStyle.None).ToList();

        [Test]
        public void TheOutputFolderIsInsideThePackage()
        {
            var root = QuickStart.PackageRoot();

            var path = DocsShot.ShotPath("reference-toggle", "overview");

            Assert.That(root, Is.Not.Null.And.Not.Empty);
            Assert.That(path, Does.StartWith(root + "/"));
            Assert.That(path, Does.EndWith("Website~/src/assets/shots/reference-toggle/overview.png"));
            Assert.That(path, Is.EqualTo(root + "/Website~/src/assets/shots/reference-toggle/overview.png"));
            Assert.That(DocsShot.ManifestPath(), Is.EqualTo(root + "/Website~/src/assets/shots/manifest.json"));
        }

        [Test]
        public void AShotNameIsKebabCase()
        {
            Assert.Throws<ArgumentException>(() => DocsShot.ShotPath("Home", "hero"));
            Assert.Throws<ArgumentException>(() => DocsShot.ShotPath("home", "../hero"));
            Assert.Throws<ArgumentException>(() => DocsShot.ShotPath("home", "hero shot"));
            Assert.Throws<ArgumentException>(() => DocsShot.ShotPath("", "hero"));
            Assert.That(DocsShot.ShotPath("reference-inspector", "overview-2"), Does.EndWith("/reference-inspector/overview-2.png"));
        }

        [Test]
        public void TheTabStripAndTheEmptyBottomAreCropped()
        {
            Assert.That(DocsShot.CropArea(21f, 500f, 360, 894, 1f), Is.EqualTo(new RectInt(0, 21, 360, 500)),
                "the tab strip above the frame and the empty space below the view go");
            Assert.That(DocsShot.CropArea(21f, 1400f, 360, 894, 1f), Is.EqualTo(new RectInt(0, 21, 360, 873)),
                "a view taller than the picture keeps every row the picture holds");
            Assert.That(DocsShot.CropArea(20f, 500f, 720, 1800, 2f), Is.EqualTo(new RectInt(0, 40, 720, 1000)),
                "points become image pixels");
        }

        [Test]
        public void ACropKeepsThePictureUpright()
        {
            // Texture rows count from the bottom; the picture's top row is the texture's last row.
            var source = new Texture2D(2, 4, TextureFormat.RGBA32, false);
            Texture2D cropped = null;
            try
            {
                var rows = new[] { Color.red, Color.green, Color.blue, Color.white };
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 2; x++)
                        source.SetPixel(x, y, rows[y]);
                source.Apply();

                cropped = InspectorShots.Crop(source, new RectInt(0, 1, 2, 2));

                Assert.That(cropped.width, Is.EqualTo(2));
                Assert.That(cropped.height, Is.EqualTo(2));
                Assert.That(cropped.GetPixel(0, 1), Is.EqualTo(Color.blue), "the top kept row is the picture's second row from the top");
                Assert.That(cropped.GetPixel(0, 0), Is.EqualTo(Color.green), "the bottom kept row is the third");
            }
            finally
            {
                Object.DestroyImmediate(source);
                if (cropped != null) Object.DestroyImmediate(cropped);
            }
        }

        [Test]
        public void AMarkerIsCutToThePicture()
        {
            var cut = DocsShot.InsidePicture(new DocsShot.Marker { N = 2, X = -4, Y = 790, W = 368, H = 40 }, 360, 812, "ad-tree");

            Assert.That((cut.N, cut.X, cut.Y, cut.W, cut.H), Is.EqualTo((2, 0, 790, 360, 22)), "a marker reaching past the edges stops at them");
            var thrown = Assert.Throws<InvalidOperationException>(() => DocsShot.InsidePicture(new DocsShot.Marker { N = 3, X = 0, Y = 900, W = 360, H = 20 }, 360, 812, "ad-search"));
            Assert.That(thrown.Message, Does.Contain("ad-search"), "a marker below the picture names its element");
        }

        [Test]
        public void AMarkedElementIsFoundByNameOrClass()
        {
            var view = new VisualElement();
            var named = new VisualElement { name = "ad-search" };
            var classed = new VisualElement();
            classed.AddToClassList("ad-membar");
            view.Add(named);
            view.Add(classed);

            Assert.That(DocsShot.FindMarked(view, "ad-search"), Is.SameAs(named));
            Assert.That(DocsShot.FindMarked(view, "ad-membar"), Is.SameAs(classed), "an element without a name is found by its class");
            var thrown = Assert.Throws<InvalidOperationException>(() => DocsShot.FindMarked(view, "ad-nothing"));
            Assert.That(thrown.Message, Does.Contain("ad-nothing"));
        }

        [Test]
        public void TheMarkerFileHasTheSizeVersionAndMarkers()
        {
            var markers = new List<DocsShot.Marker> { DocsShot.ToMarker(new Rect(0f, 0f, 360f, 900f), new Rect(4f, 8f, 100f, 22f), 1f, 1) };

            var file = (DocsJson.Map)DocsJson.Parse(DocsShot.MarkerJson(360, 812, "0.3.0", markers));

            Assert.That(file.Keys, Is.EqualTo(new[] { "width", "height", "version", "markers" }));
            Assert.That(file["width"], Is.EqualTo(360d));
            Assert.That(file["height"], Is.EqualTo(812d));
            Assert.That(file["version"], Is.EqualTo("0.3.0"));
            var marker = (DocsJson.Map)((List<object>)file["markers"]).Single();
            Assert.That(marker.Keys, Is.EqualTo(new[] { "n", "x", "y", "w", "h" }));
            Assert.That(new[] { marker["n"], marker["x"], marker["y"], marker["w"], marker["h"] }, Is.EqualTo(new object[] { 1d, 4d, 8d, 100d, 22d }));
            Assert.That(DocsShot.PackageVersion(), Does.Match(@"^\d+\.\d+\.\d+"), "the version comes from the package");
        }

        [Test]
        public void TheDocsAvatarHasEveryPartAndLeavesNothingBehind()
        {
            int testBodyMeshes = MeshesWithShape("Eye_Big");
            var avatar = DocsAvatar.Create();
            var made = new List<Object>();
            try
            {
                Assert.That(ShapeNames(avatar.Body.sharedMesh), Is.EqualTo(new[] { "Chest_Big", "Shrink_Feet" }), "Body has body shapes, not the TestAvatar's");
                Assert.That(MeshesWithShape("Eye_Big"), Is.EqualTo(testBodyMeshes), "the TestAvatar's own body mesh is destroyed when Body gets its own");
                Assert.That(new Object[] { avatar.Root, avatar.Body, avatar.Face, avatar.Hat, avatar.Jacket, avatar.Hoodie, avatar.Glasses, avatar.Shoes, avatar.HeadContact, avatar.HairBone },
                    Has.None.Null);
                Assert.That(new[] { avatar.Skin.name, avatar.Cloth.name, avatar.Metal.name }, Is.EqualTo(new[] { "Skin", "Cloth", "Metal" }));
                Assert.That(new[] { avatar.Skin, avatar.Cloth, avatar.Metal }.Select(m => m.shader.name), Is.All.EqualTo("Standard"));
                Assert.That(new[] { avatar.Hat, avatar.Jacket, avatar.Hoodie, avatar.Glasses, avatar.Shoes }.Select(o => o.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null), Has.None.Null,
                    "the clothes and glasses are mesh objects");
                var face = avatar.Face.sharedMesh;
                foreach (var shape in new[] { "Blush", "Smile", "Smile_Eyes", "Mouth_Smile_Wide", "Brow_Angry", "Eye_Wide", "Cheek_Puff", "Tongue_Out", "vrc.blink", "vrc.v_aa", "vrc.v_oh", "vrc.v_ih" })
                    Assert.That(face.GetBlendShapeIndex(shape), Is.GreaterThanOrEqualTo(0), shape);
                Assert.That(avatar.Root.GetComponent<Animator>().isHuman, Is.True, "a humanoid root");
                Assert.That(avatar.HeadContact.IsChildOf(avatar.Root.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head)), Is.True, "the contact anchor sits on the head");

                var eyes = avatar.Descriptor.customEyeLookSettings;
                Assert.That(avatar.Descriptor.enableEyeLook, Is.True);
                Assert.That(eyes.eyelidsSkinnedMesh, Is.SameAs(avatar.Face));
                Assert.That(eyes.eyelidsBlendshapes[0], Is.EqualTo(face.GetBlendShapeIndex("vrc.blink")), "VRChat blinks with the face's blink shape");
                Assert.That(avatar.Descriptor.lipSync, Is.EqualTo(VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape));
                Assert.That(avatar.Descriptor.VisemeSkinnedMesh, Is.SameAs(avatar.Face));
                Assert.That(avatar.Descriptor.VisemeBlendShapes[(int)VRC_AvatarDescriptor.Viseme.aa], Is.EqualTo("vrc.v_aa"));
                Assert.That(avatar.Descriptor.VisemeBlendShapes[(int)VRC_AvatarDescriptor.Viseme.oh], Is.EqualTo("vrc.v_oh"));
                Assert.That(avatar.Descriptor.VisemeBlendShapes[(int)VRC_AvatarDescriptor.Viseme.ih], Is.EqualTo("vrc.v_ih"));

                made.AddRange(new Object[] { avatar.Root, face, avatar.Body.sharedMesh, avatar.Hat.GetComponent<MeshFilter>().sharedMesh, avatar.Skin, avatar.Cloth, avatar.Metal });
            }
            finally
            {
                avatar.Dispose();
            }
            Assert.That(made, Has.All.Matches<Object>(o => o == null), "Dispose destroys the avatar, its meshes and its materials");
        }

        static int MeshesWithShape(string shape) => Resources.FindObjectsOfTypeAll<Mesh>().Count(mesh => mesh.GetBlendShapeIndex(shape) >= 0);

        static string[] ShapeNames(Mesh mesh) => Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToArray();

        static DocsJson.Map Shots(string manifest) => (DocsJson.Map)((DocsJson.Map)DocsJson.Parse(manifest))["shots"];

        static object Field(DocsJson.Map shots, string shot, string field) => ((DocsJson.Map)shots[shot])[field];
    }
}
#endif
