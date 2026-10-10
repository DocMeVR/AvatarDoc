#if DOCME_NDMF && DOCME_VRCSDK3A
using System.IO;
using System.Linq;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace DocMe.AvatarDoctor.Editor
{
    public class OutfitSetTests
    {
        TestAvatar avatar;
        AvatarDoc menu;

        [SetUp]
        public void SetUp()
        {
            GeneratedAssets.Note();
            avatar = TestAvatar.Create();
            menu = avatar.AddMenu();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            GeneratedAssets.CleanUp();
        }

        static Outfit Of(string name, params GameObject[] pieces) => new Outfit { Name = name, Pieces = pieces.ToList() };

        [Test]
        public void EmptySetNameStillBuildsOneGroup()
        {
            var spec = new OutfitSpec
            {
                SetName = "   ",
                Mode = OutfitMode.OneAtATime,
                Outfits = { Of("Casual", avatar.Hat), Of("Formal", avatar.Glasses) },
                DefaultOutfit = 0,
            };

            var created = OutfitSetBuilder.Apply(menu, spec, out var adjusted);

            var expected = L.Text("ad.outfit.default_name");
            Assert.That(adjusted, Is.EqualTo(expected));
            Assert.That(created[0].name, Is.EqualTo(expected), "the submenu is named too, rather than left blank");
            var toggles = menu.entries.Where(e => e.kind == EntryKind.Toggle).ToList();
            Assert.That(toggles.Count, Is.EqualTo(2));
            Assert.That(toggles.Select(t => t.exclusiveGroup).Distinct().ToList(), Is.EqualTo(new[] { expected }), "both outfits are in one group");
        }

        [Test]
        public void SecondSetWithTheSameNameGetsItsOwnGroup()
        {
            OutfitSetBuilder.Apply(menu, new OutfitSpec
            {
                SetName = "Outfits",
                Mode = OutfitMode.OneAtATime,
                Outfits = { Of("Casual", avatar.Hat) },
                DefaultOutfit = 0,
            });

            var created = OutfitSetBuilder.Apply(menu, new OutfitSpec
            {
                SetName = "Outfits",
                Mode = OutfitMode.OneAtATime,
                Outfits = { Of("Formal", avatar.Glasses) },
                DefaultOutfit = 0,
            }, out var adjusted);

            Assert.That(adjusted, Is.EqualTo("Outfits 2"));
            var formal = menu.entries.Single(e => e.name == "Formal");
            Assert.That(formal.exclusiveGroup, Is.EqualTo("Outfits 2"));
            Assert.That(menu.entries.Single(e => e.name == "Casual").exclusiveGroup, Is.EqualTo("Outfits"), "the first set keeps its own group");
            Assert.That(created[0].kind, Is.EqualTo(EntryKind.Submenu));
        }

        [Test]
        public void StyledSetNameStaysOutOfTheGroupName()
        {
            var spec = new OutfitSpec
            {
                SetName = "<color=#FF0000>Party</color>",
                Mode = OutfitMode.OneAtATime,
                Outfits = { Of("Casual", avatar.Hat) },
                DefaultOutfit = 0,
            };

            var created = OutfitSetBuilder.Apply(menu, spec, out var adjusted);

            Assert.That(adjusted, Is.EqualTo("Party"));
            Assert.That(created[0].name, Is.EqualTo("<color=#FF0000>Party</color>"), "the submenu keeps the styling");
            Assert.That(menu.entries.Single(e => e.name == "Casual").exclusiveGroup, Is.EqualTo("Party"));
        }

        [Test]
        public void OneAtATimeBuildsAGroupWithADefault()
        {
            var spec = new OutfitSpec
            {
                SetName = "Outfits",
                Mode = OutfitMode.OneAtATime,
                Outfits = { Of("Casual", avatar.Hat), Of("Formal", avatar.Glasses) },
                DefaultOutfit = 0,
            };

            var created = OutfitSetBuilder.Apply(menu, spec);

            var submenu = created[0];
            Assert.That(submenu.kind, Is.EqualTo(EntryKind.Submenu));
            Assert.That(submenu.name, Is.EqualTo("Outfits"));
            Assert.That(submenu.parentId, Is.Empty);

            var casual = menu.entries.Single(e => e.name == "Casual");
            var formal = menu.entries.Single(e => e.name == "Formal");
            Assert.That(casual.kind, Is.EqualTo(EntryKind.Toggle));
            Assert.That(casual.parentId, Is.EqualTo(submenu.id));
            Assert.That(casual.exclusiveGroup, Is.EqualTo("Outfits"));
            Assert.That(formal.exclusiveGroup, Is.EqualTo("Outfits"));
            Assert.That(casual.defaultOn, Is.True);
            Assert.That(formal.defaultOn, Is.False);
            Assert.That(casual.objects.Select(o => o.target), Is.EquivalentTo(new[] { avatar.Hat }));
            Assert.That(casual.objects.All(o => o.activeWhenOn), Is.True);
        }

        [Test]
        public void NoneOptionShowsNothing()
        {
            var spec = new OutfitSpec
            {
                SetName = "Outfits",
                Mode = OutfitMode.OneAtATime,
                Outfits = { Of("Casual", avatar.Hat) },
                AddNoneOption = true,
            };

            OutfitSetBuilder.Apply(menu, spec);

            var none = menu.entries.Single(e => e.name == L.Text("ad.outfit.none"));
            Assert.That(none.kind, Is.EqualTo(EntryKind.Toggle));
            Assert.That(none.exclusiveGroup, Is.EqualTo("Outfits"));
            Assert.That(none.objects, Is.Empty);
            Assert.That(none.blendShapes, Is.Empty);
        }

        [Test]
        public void NoneIsTheDefaultWhenNoOutfitIs()
        {
            var spec = new OutfitSpec
            {
                SetName = "Outfits",
                Mode = OutfitMode.OneAtATime,
                Outfits = { Of("Casual", avatar.Hat), Of("Formal", avatar.Glasses) },
                AddNoneOption = true,
                DefaultOutfit = 5,
            };

            OutfitSetBuilder.Apply(menu, spec);

            var casual = menu.entries.Single(e => e.name == "Casual");
            var formal = menu.entries.Single(e => e.name == "Formal");
            var none = menu.entries.Single(e => e.name == L.Text("ad.outfit.none"));
            Assert.That(casual.defaultOn, Is.False);
            Assert.That(formal.defaultOn, Is.False);
            Assert.That(none.defaultOn, Is.True);
        }

        [Test]
        public void HiddenPiecesAreHiddenByEveryOutfit()
        {
            var shoes = new GameObject("Shoes");
            shoes.transform.SetParent(avatar.Root.transform);
            var spec = new OutfitSpec
            {
                SetName = "Outfits",
                Mode = OutfitMode.OneAtATime,
                Outfits = { Of("Casual", avatar.Hat), Of("Formal", avatar.Glasses) },
                HiddenWhileAnyOutfit = { shoes },
            };

            OutfitSetBuilder.Apply(menu, spec);

            foreach (var name in new[] { "Casual", "Formal" })
            {
                var toggle = menu.entries.Single(e => e.name == name);
                var hide = toggle.objects.Single(o => o.target == shoes);
                Assert.That(hide.activeWhenOn, Is.False, name);
            }
        }

        [Test]
        public void HiddenPiecesComeBackWhenNoneIsChosen()
        {
            var shoes = new GameObject("Shoes");
            shoes.transform.SetParent(avatar.Root.transform);
            var spec = new OutfitSpec
            {
                SetName = "Outfits",
                Mode = OutfitMode.OneAtATime,
                Outfits = { Of("Casual", avatar.Hat), Of("Formal", avatar.Glasses) },
                HiddenWhileAnyOutfit = { shoes },
                AddNoneOption = true,
            };
            OutfitSetBuilder.Apply(menu, spec);

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var fx = processed.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers
                    .Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
                var layer = fx.layers.Single(l => l.name == "AD: Outfits");
                var casual = layer.stateMachine.states.Single(s => s.state.name == "Casual").state;
                var none = layer.stateMachine.states.Single(s => s.state.name == L.Text("ad.outfit.none")).state;

                Assert.That(GameObjectFloat(casual.motion, "Shoes"), Is.EqualTo(0f), "an outfit selected hides the piece");
                Assert.That(GameObjectFloat(none.motion, "Shoes"), Is.EqualTo(1f), "None selected shows the piece again");
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
            }
        }

        static float GameObjectFloat(Motion motion, string path)
        {
            var curve = AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive"));
            Assert.That(curve, Is.Not.Null, path);
            return curve.Evaluate(0f);
        }

        [Test]
        public void MixFreelyBuildsTogglesAndPresets()
        {
            var shared = new GameObject("Shared");
            shared.transform.SetParent(avatar.Root.transform);
            var spec = new OutfitSpec
            {
                SetName = "Outfits",
                Mode = OutfitMode.MixFreely,
                Outfits = { Of("Casual", avatar.Hat, shared), Of("Formal", avatar.Glasses, shared) },
                DefaultOutfit = 0,
            };

            var created = OutfitSetBuilder.Apply(menu, spec);

            var toggles = menu.entries.Where(e => e.kind == EntryKind.Toggle).ToList();
            Assert.That(toggles.Select(t => t.name), Is.EquivalentTo(new[] { "Hat", "Shared", "Glasses" }));
            Assert.That(toggles.Count, Is.EqualTo(3), "a piece shared by two outfits gets one toggle");
            var hatToggle = toggles.Single(t => t.name == "Hat");
            var sharedToggle = toggles.Single(t => t.name == "Shared");
            var glassesToggle = toggles.Single(t => t.name == "Glasses");
            Assert.That(hatToggle.defaultOn, Is.True);
            Assert.That(sharedToggle.defaultOn, Is.True);
            Assert.That(glassesToggle.defaultOn, Is.False);

            var casualPreset = menu.entries.Single(e => e.kind == EntryKind.Preset && e.name == "Casual");
            Assert.That(casualPreset.presetStates.Count, Is.EqualTo(3), "a preset lists every piece toggle of the set");
            Assert.That(casualPreset.presetStates.Single(s => s.entryId == hatToggle.id).on, Is.True);
            Assert.That(casualPreset.presetStates.Single(s => s.entryId == sharedToggle.id).on, Is.True);
            Assert.That(casualPreset.presetStates.Single(s => s.entryId == glassesToggle.id).on, Is.False);

            Assert.That(created.First(), Is.SameAs(menu.entries.Single(e => e.kind == EntryKind.Submenu)));
        }

        [Test]
        public void EverythingOffPresetSwitchesAllPiecesOff()
        {
            var spec = new OutfitSpec
            {
                SetName = "Outfits",
                Mode = OutfitMode.MixFreely,
                Outfits = { Of("Casual", avatar.Hat), Of("Formal", avatar.Glasses) },
            };

            OutfitSetBuilder.Apply(menu, spec);

            var off = menu.entries.Single(e => e.kind == EntryKind.Preset && e.name == L.Text("ad.outfit.everything_off"));
            var toggleIds = menu.entries.Where(e => e.kind == EntryKind.Toggle).Select(e => e.id).ToList();
            Assert.That(off.presetStates.Count, Is.EqualTo(toggleIds.Count));
            Assert.That(off.presetStates.All(s => !s.on), Is.True);
            Assert.That(off.presetStates.Select(s => s.entryId), Is.EquivalentTo(toggleIds));
        }

        [Test]
        public void ChosenShrinksBecomeBlendShapeTargets()
        {
            var chosen = new ShrinkSuggestion { Renderer = avatar.Body, Shape = "Blush", Piece = avatar.Hat, Chosen = true, Weight = 80f };
            var declined = new ShrinkSuggestion { Renderer = avatar.Body, Shape = "Eye_Big", Piece = avatar.Hat, Chosen = false };
            var spec = new OutfitSpec
            {
                SetName = "Outfits",
                Mode = OutfitMode.OneAtATime,
                Outfits = { new Outfit { Name = "Casual", Pieces = { avatar.Hat }, Shrinks = { chosen, declined } } },
            };

            OutfitSetBuilder.Apply(menu, spec);

            var toggle = menu.entries.Single(e => e.name == "Casual");
            Assert.That(toggle.blendShapes.Count, Is.EqualTo(1));
            Assert.That(toggle.blendShapes[0].renderer, Is.SameAs(avatar.Body));
            Assert.That(toggle.blendShapes[0].shape, Is.EqualTo("Blush"));
            Assert.That(toggle.blendShapes[0].weight, Is.EqualTo(80f));
        }

        [Test]
        public void ApplyIsOneUndoStep()
        {
            var spec = new OutfitSpec
            {
                SetName = "Outfits",
                Mode = OutfitMode.OneAtATime,
                Outfits = { Of("Casual", avatar.Hat), Of("Formal", avatar.Glasses) },
            };

            Undo.IncrementCurrentGroup();
            var created = OutfitSetBuilder.Apply(menu, spec);
            Assert.That(created, Is.Not.Empty);
            Assert.That(menu.entries.Count, Is.EqualTo(created.Count));

            Undo.PerformUndo();

            Assert.That(menu.entries, Is.Empty);
        }

        [Test]
        public void BuiltSetSurvivesTheFullPipeline()
        {
            var spec = new OutfitSpec
            {
                SetName = "Outfits",
                Mode = OutfitMode.OneAtATime,
                Outfits = { Of("Casual", avatar.Hat), Of("Formal", avatar.Glasses) },
                DefaultOutfit = 0,
            };
            OutfitSetBuilder.Apply(menu, spec);

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var descriptor = processed.GetComponent<VRCAvatarDescriptor>();
                var names = descriptor.expressionsMenu.controls.Select(c => c.name).ToList();
                Assert.That(names, Does.Contain("Outfits"));
                Assert.That(processed.GetComponentsInChildren<AvatarDoc>(true), Is.Empty);
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
            }
        }

        [Test]
        public void ShrinkSuggesterMatchesByName()
        {
            var mesh = TestAvatar.QuadWithShapes("Shrink_Hat", "hat_off", "Unrelated");
            avatar.Body.sharedMesh = mesh;
            try
            {
                var suggestions = ShrinkSuggester.For(avatar.Root.transform, avatar.Hat);

                Assert.That(suggestions.Select(s => s.Shape), Is.EquivalentTo(new[] { "Shrink_Hat", "hat_off" }));
                Assert.That(suggestions.All(s => s.Renderer == avatar.Body), Is.True);
                Assert.That(suggestions.All(s => s.Piece == avatar.Hat), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void ShrinkSuggesterIgnoresShortAndOwnNames()
        {
            var ownMesh = TestAvatar.QuadWithShapes("Shrink_Hat");
            avatar.Hat.AddComponent<SkinnedMeshRenderer>().sharedMesh = ownMesh;
            var bodyMesh = TestAvatar.QuadWithShapes("Ha");
            avatar.Body.sharedMesh = bodyMesh;
            try
            {
                var suggestions = ShrinkSuggester.For(avatar.Root.transform, avatar.Hat);

                Assert.That(suggestions, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(ownMesh);
                Object.DestroyImmediate(bodyMesh);
            }
        }

        [Test]
        public void CaptureMissingOnlyFillsEmptyIcons()
        {
            const string TempParent = "Assets/TempAssets";
            const string Folder = TempParent + "/AvatarDoctor Icons";
            if (!AssetDatabase.IsValidFolder(TempParent)) AssetDatabase.CreateFolder("Assets", "TempAssets");
            IconCapture.RootFolder = Folder;
            try
            {
                avatar.Hat.GetComponent<MeshFilter>().sharedMesh = TestAvatar.QuadWithShapes();
                avatar.Hat.GetComponent<MeshRenderer>().sharedMaterial = avatar.MaterialA;
                avatar.Hat.transform.position = new Vector3(0f, 0f, 5f);

                var already = MenuEntry.Create(EntryKind.Toggle, "Already");
                already.objects.Add(new ObjectTarget { target = avatar.Hat });
                var existing = new Texture2D(4, 4);
                already.icon = existing;
                menu.entries.Add(already);

                var missing = MenuEntry.Create(EntryKind.Toggle, "Missing");
                missing.objects.Add(new ObjectTarget { target = avatar.Hat });
                menu.entries.Add(missing);

                var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

                int made = IconBatch.CaptureMissing(menu, tree, avatar.Root.transform);

                Assert.That(made, Is.EqualTo(1));
                Assert.That(already.icon, Is.SameAs(existing), "the entry with an icon already is left alone");
                Assert.That(missing.icon, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(missing.icon), Does.StartWith(Folder + "/"));
                Object.DestroyImmediate(existing);
            }
            finally
            {
                IconCapture.RootFolder = IconCapture.Folder;
                if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
                if (AssetDatabase.IsValidFolder(TempParent) && AssetDatabase.FindAssets("", new[] { TempParent }).Length == 0) AssetDatabase.DeleteAsset(TempParent);
            }
        }
    }
}
#endif
