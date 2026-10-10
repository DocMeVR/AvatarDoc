#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Renders a control's pieces alone, against a transparent background, and saves the result as a menu icon. Nothing in the scene changes.</summary>
    public static class IconCapture
    {
        const string IconsFolderLeaf = "AvatarDoc Icons";
        const string LegacyIconsFolderLeaf = "AvatarDoctor Icons";

        /// <summary>The icons folder under the given parent: the AvatarDoc name, unless the parent already has the
        /// old AvatarDoctor Icons folder and no AvatarDoc one yet, so a creator's icons are not split across two
        /// folders.</summary>
        public static string ResolveFolder(string parent)
        {
            string legacy = parent + "/" + LegacyIconsFolderLeaf;
            string current = parent + "/" + IconsFolderLeaf;
            return AssetDatabase.IsValidFolder(legacy) && !AssetDatabase.IsValidFolder(current) ? legacy : current;
        }

        /// <summary>The default icons root: <see cref="ResolveFolder"/> under "Assets".</summary>
        public static string Folder => ResolveFolder("Assets");
        /// <summary>Where saved icons go. It is the folder above by default; the tests point it at a temporary folder of their own.</summary>
        public static string RootFolder = Folder;
        const string CameraName = "AvatarDoctor Capture Camera";

        /// <summary>How many capture cameras were made. Read by the tests.</summary>
        public static int CamerasBuilt { get; private set; }

        public struct Frame
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public float FieldOfView;
            public bool Orthographic;
            public float OrthographicSize;
        }

        public static Frame FromSceneView()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null || view.camera == null)
                return new Frame { Position = new Vector3(0f, 1f, -3f), Rotation = Quaternion.identity, FieldOfView = 40f };
            var cam = view.camera;
            return new Frame { Position = cam.transform.position, Rotation = cam.transform.rotation, FieldOfView = cam.fieldOfView, Orthographic = cam.orthographic, OrthographicSize = cam.orthographicSize };
        }

        public static List<Renderer> TargetsOf(MenuEntry entry, MenuTree tree, Transform avatarRoot)
        {
            var result = new List<Renderer>();
            if (entry == null) return result;
            var seen = new HashSet<Renderer>();
            void Add(Renderer renderer)
            {
                if (renderer != null && !MenuModel.IsOutsideAvatar(avatarRoot, renderer.transform) && seen.Add(renderer)) result.Add(renderer);
            }
            void AddEntry(MenuEntry e)
            {
                foreach (var target in e.objects)
                    if (target.target != null)
                        foreach (var renderer in RenderersUnder(target.target)) Add(renderer);
                foreach (var target in e.blendShapes) Add(target.renderer);
                foreach (var target in e.materials) Add(target.renderer);
                foreach (var target in e.properties) Add(target.renderer);
            }
            if (entry.kind == EntryKind.Preset)
            {
                foreach (var state in entry.presetStates)
                {
                    var target = tree.FindControl(state.entryId);
                    if (target == null) continue;
                    if (target.Entry.kind != EntryKind.Radial && !state.on) continue;
                    AddEntry(target.Entry);
                }
            }
            else if (entry.kind == EntryKind.Submenu)
            {
                var component = tree.Components.FirstOrDefault(c => c.entries.Contains(entry));
                if (component != null)
                    foreach (var child in EntryEditing.Descendants(component, entry))
                        if (!child.IsRule && child.kind != EntryKind.Submenu) AddEntry(child);
            }
            else AddEntry(entry);
            return result;
        }

        /// <summary>Enabled renderers under a target object whose own chain below the target is active: only the target itself may be switched off.</summary>
        static IEnumerable<Renderer> RenderersUnder(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled) continue;
                bool active = true;
                for (var t = renderer.transform; t != null && t != root.transform; t = t.parent)
                    if (!t.gameObject.activeSelf) { active = false; break; }
                if (active) yield return renderer;
            }
        }

        public static Bounds BoundsOf(IReadOnlyList<Renderer> targets)
        {
            if (targets.Count == 0) return new Bounds(Vector3.zero, Vector3.one);
            var bounds = targets[0].bounds;
            for (int i = 1; i < targets.Count; i++) bounds.Encapsulate(targets[i].bounds);
            return bounds;
        }

        /// <summary>Keeps the frame's direction and moves the camera so the bounds fill the vertical field of view with some padding.</summary>
        public static Frame Fit(Frame frame, Bounds bounds)
        {
            var forward = frame.Rotation * Vector3.forward;
            float radius = Mathf.Max(bounds.extents.magnitude, 0.05f) * 1.1f;
            var fitted = frame;
            if (frame.Orthographic)
            {
                fitted.OrthographicSize = Mathf.Max(radius, 0.01f);
                fitted.Position = bounds.center - forward * Mathf.Max(radius * 4f, 1f);
                return fitted;
            }
            float distance = radius / Mathf.Tan(Mathf.Max(frame.FieldOfView, 1f) * 0.5f * Mathf.Deg2Rad);
            fitted.Position = bounds.center - forward * distance;
            return fitted;
        }

        /// <summary>Renders the targets alone through a camera made for this one call. The capture window keeps a Rig instead, for as long as it is open.</summary>
        public static Texture2D Render(Frame frame, IReadOnlyList<Renderer> targets, int size, Color background)
        {
            using (var rig = new Rig()) return rig.Render(frame, targets, size, background);
        }

        /// <summary>
        /// The hidden camera a capture renders through, made once and reused for every render until disposed, so a
        /// preview that follows the Scene view does not make and destroy scene objects for every frame. Its culling
        /// mask is worked out again for every render from the targets and the lights as they are then.
        /// </summary>
        public sealed class Rig : IDisposable
        {
            GameObject holder;
            Camera camera;
            readonly Dictionary<int, RenderTexture> textures = new Dictionary<int, RenderTexture>();

            /// <summary>The camera, or null once the rig is disposed. Read by the tests.</summary>
            public Camera Camera => camera;

            public Rig()
            {
                Build();
            }

            void Build()
            {
                CamerasBuilt++;
                // NDMF's preview hooks any camera that has no parent or no target texture, and while the hook runs it
                // switches the proxied originals off and their proxies on. A camera parented under this holder, with
                // a target texture while it renders, is left alone, so the capture shows the pieces themselves.
                holder = new GameObject(CameraName) { hideFlags = HideFlags.HideAndDontSave };
                var cameraObject = new GameObject("Camera") { hideFlags = HideFlags.HideAndDontSave };
                cameraObject.transform.SetParent(holder.transform, false);
                camera = cameraObject.AddComponent<Camera>();
                // Switched off, so it draws only when Render asks it to and never into the Game view.
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 1000f;
                // Every picture is square.
                camera.aspect = 1f;
            }

            /// <summary>A camera destroyed from outside (a scene unloaded with it, say) is made again rather than used.</summary>
            void Ensure()
            {
                if (camera != null) return;
                if (holder != null) Object.DestroyImmediate(holder);
                ReleaseTextures();
                Build();
            }

            RenderTexture TextureOf(int size)
            {
                if (textures.TryGetValue(size, out var texture) && texture != null) return texture;
                texture = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
                textures[size] = texture;
                return texture;
            }

            void ReleaseTextures()
            {
                foreach (var texture in textures.Values)
                    if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
                textures.Clear();
            }

            public Texture2D Render(Frame frame, IReadOnlyList<Renderer> targets, int size, Color background)
            {
                Ensure();
                var hidden = new List<Renderer>();
                var targetSet = new HashSet<Renderer>(targets);
                var previous = RenderTexture.active;
                var baked = new List<Mesh>();
                CommandBuffer switchedOff = null;
                var texture = TextureOf(size);
                try
                {
                    foreach (var renderer in Object.FindObjectsOfType<Renderer>(true))
                        if (!targetSet.Contains(renderer) && !renderer.forceRenderingOff) { renderer.forceRenderingOff = true; hidden.Add(renderer); }
                    camera.transform.SetPositionAndRotation(frame.Position, frame.Rotation);
                    camera.fieldOfView = frame.FieldOfView;
                    camera.orthographic = frame.Orthographic;
                    camera.orthographicSize = frame.OrthographicSize;
                    camera.backgroundColor = background;
                    camera.cullingMask = CaptureMask(targets);
                    camera.targetTexture = texture;
                    switchedOff = DrawSwitchedOff(targets, baked);
                    if (switchedOff != null) camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, switchedOff);
                    camera.Render();
                    RenderTexture.active = texture;
                    var result = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    result.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    result.Apply();
                    result.hideFlags = HideFlags.HideAndDontSave;
                    return result;
                }
                finally
                {
                    foreach (var renderer in hidden) if (renderer != null) renderer.forceRenderingOff = false;
                    // The active target has to go back before a texture can die, on the way out of a throw as well.
                    RenderTexture.active = previous;
                    if (switchedOff != null)
                    {
                        if (camera != null) camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, switchedOff);
                        switchedOff.Dispose();
                    }
                    // A camera holding no texture between renders leaves nothing for a later release to warn about.
                    if (camera != null) camera.targetTexture = null;
                    foreach (var mesh in baked) Object.DestroyImmediate(mesh);
                }
            }

            /// <summary>See IconCapture.RenderFitted.</summary>
            public Texture2D RenderFitted(Frame frame, IReadOnlyList<Renderer> targets, int size, Color background, float margin = 0.06f, int workingSize = WorkingSize)
            {
                var working = Render(frame, targets, workingSize, new Color(0f, 0f, 0f, 0f));
                try
                {
                    if (!TryOpaqueBounds(working, AlphaThreshold, out var box))
                        return Render(frame, targets, size, background);

                    var square = SquareAround(box, workingSize, margin);
                    if (square.Side < size * 2f)
                    {
                        float centerU = (square.X + square.Side * 0.5f) / workingSize;
                        float centerV = (square.Y + square.Side * 0.5f) / workingSize;
                        float scale = square.Side / workingSize;
                        var narrowed = Narrow(frame, centerU, centerV, scale);
                        var refined = Render(narrowed, targets, workingSize, new Color(0f, 0f, 0f, 0f));
                        Object.DestroyImmediate(working);
                        working = refined;
                        if (!TryOpaqueBounds(working, AlphaThreshold, out box))
                            return Render(frame, targets, size, background);
                        square = SquareAround(box, workingSize, margin);
                    }
                    return CropScaleComposite(working, square, size, background);
                }
                finally
                {
                    Object.DestroyImmediate(working);
                }
            }

            /// <summary>Re-aims the frame so the square, given as a viewport centre and a scale relative to the whole frame, fills the whole view: an orthographic camera shifts and shrinks in its own plane; a perspective one re-aims through the square's centre, with the field of view narrowed by the same scale, using the rig's camera only for the viewport math.</summary>
            Frame Narrow(Frame frame, float centerU, float centerV, float scale)
            {
                var narrowed = frame;
                if (frame.Orthographic)
                {
                    var right = frame.Rotation * Vector3.right;
                    var up = frame.Rotation * Vector3.up;
                    float fullSize = frame.OrthographicSize * 2f;
                    narrowed.Position = frame.Position + right * ((centerU - 0.5f) * fullSize) + up * ((centerV - 0.5f) * fullSize);
                    narrowed.OrthographicSize = Mathf.Max(frame.OrthographicSize * scale, 0.0001f);
                    return narrowed;
                }
                Ensure();
                camera.transform.SetPositionAndRotation(frame.Position, frame.Rotation);
                camera.orthographic = false;
                camera.fieldOfView = frame.FieldOfView;
                var ray = camera.ViewportPointToRay(new Vector3(centerU, centerV, 0f));
                var aimUp = frame.Rotation * Vector3.up;
                narrowed.Rotation = Quaternion.LookRotation(ray.direction, aimUp);
                float halfOld = Mathf.Max(frame.FieldOfView, 1f) * 0.5f * Mathf.Deg2Rad;
                float newHalfTan = Mathf.Tan(halfOld) * scale;
                narrowed.FieldOfView = Mathf.Atan(newHalfTan) * 2f * Mathf.Rad2Deg;
                return narrowed;
            }

            public void Dispose()
            {
                if (camera != null) camera.targetTexture = null;
                ReleaseTextures();
                if (holder != null) Object.DestroyImmediate(holder);
                holder = null;
                camera = null;
            }
        }

        /// <summary>
        /// The layers the capture camera looks at: the targets' own, and those of every enabled light in the open
        /// scenes. Switching other renderers off leaves anything that is not a Renderer drawing, a world-space
        /// canvas first of all (CanvasRenderer is not a Renderer), so the mask holds nothing else. The built-in
        /// pipeline leaves a light out of a camera's render when the light's object is on a layer outside the
        /// camera's mask, so the lights' layers have to be in it or the pieces would be drawn unlit. The draws for
        /// pieces switched off in the scene are a command buffer, which culling does not reach.
        /// </summary>
        public static int CaptureMask(IReadOnlyList<Renderer> targets)
        {
            int mask = 0;
            foreach (var renderer in targets)
                if (renderer != null) mask |= 1 << renderer.gameObject.layer;
            foreach (var light in Object.FindObjectsOfType<Light>())
                if (light.enabled) mask |= 1 << light.gameObject.layer;
            return mask;
        }

        /// <summary>
        /// Collects a draw for every target piece that is switched off in the scene, so a toggle that starts off still
        /// gets a picture. Nothing is switched on: the mesh is drawn as it stands, with the blend shape and material
        /// values it has right now. A skinned piece is baked into a temporary mesh the caller destroys. Returns null
        /// when every target is on. Graphics.DrawMesh queues for a frame the edit mode camera never reaches, so the
        /// draws go on the camera as a command buffer instead.
        /// </summary>
        static CommandBuffer DrawSwitchedOff(IReadOnlyList<Renderer> targets, List<Mesh> baked)
        {
            CommandBuffer buffer = null;
            foreach (var renderer in targets)
            {
                if (renderer == null || !renderer.enabled || renderer.gameObject.activeInHierarchy) continue;
                Mesh mesh;
                Matrix4x4 matrix;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    if (skinned.sharedMesh == null) continue;
                    mesh = new Mesh { name = "AvatarDoctor Capture Bake", hideFlags = HideFlags.HideAndDontSave };
                    skinned.BakeMesh(mesh, true);
                    baked.Add(mesh);
                    matrix = Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
                }
                else
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    mesh = filter.sharedMesh;
                    matrix = renderer.localToWorldMatrix;
                }
                MaterialPropertyBlock block = null;
                if (renderer.HasPropertyBlock()) { block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); }
                var materials = renderer.sharedMaterials;
                int count = Mathf.Min(materials.Length, mesh.subMeshCount);
                for (int i = 0; i < count; i++)
                {
                    if (materials[i] == null) continue;
                    if (buffer == null) buffer = new CommandBuffer { name = "AvatarDoctor Capture" };
                    buffer.DrawMesh(mesh, matrix, materials[i], i, -1, block);
                }
            }
            return buffer;
        }

        /// <summary>Working size of a fitted render that gets saved. The live preview asks for PreviewWorkingSize instead, which costs a quarter of the pixels while the Scene view is being moved.</summary>
        public const int WorkingSize = 1024;
        public const int PreviewWorkingSize = 512;
        const float AlphaThreshold = 0.02f;

        struct SquareRegion
        {
            public float X, Y, Side;
        }

        /// <summary>
        /// Renders once at a large working size, finds the square around what is actually drawn (the loose renderer
        /// bounds are not used here), and produces the final icon from that square: cropped, scaled with a bilinear
        /// blit and, for a solid background, composited after the crop so the alpha threshold used to find the
        /// content never changes a pixel's own alpha. When the square is still small in the working image, a second
        /// pass narrows the camera onto it first so the icon stays sharp instead of being upscaled. An empty render
        /// (no pixel above the alpha threshold) is returned unchanged, at the requested size and background.
        /// </summary>
        public static Texture2D RenderFitted(Frame frame, IReadOnlyList<Renderer> targets, int size, Color background, float margin = 0.06f, int workingSize = WorkingSize)
        {
            using (var rig = new Rig()) return rig.RenderFitted(frame, targets, size, background, margin, workingSize);
        }

        /// <summary>The square around a content box: side is the longer edge times the margin factor, clamped to the image by shifting, never by shrinking below the content.</summary>
        static SquareRegion SquareAround(RectInt box, int imageSize, float margin)
        {
            float longer = Mathf.Max(box.width, box.height);
            float side = Mathf.Min(longer * (1f + 2f * margin), imageSize);
            float centerX = box.x + box.width * 0.5f;
            float centerY = box.y + box.height * 0.5f;
            float x = Mathf.Clamp(centerX - side * 0.5f, 0f, imageSize - side);
            float y = Mathf.Clamp(centerY - side * 0.5f, 0f, imageSize - side);
            return new SquareRegion { X = x, Y = y, Side = side };
        }

        /// <summary>The bounding box of pixels whose alpha is at or above the threshold. False when nothing is.</summary>
        static bool TryOpaqueBounds(Texture2D texture, float alphaThreshold, out RectInt box)
        {
            var pixels = texture.GetPixels32();
            int size = texture.width;
            byte threshold = (byte)Mathf.Clamp(Mathf.RoundToInt(alphaThreshold * 255f), 1, 255);
            int minX = size, minY = size, maxX = -1, maxY = -1;
            for (int y = 0; y < size; y++)
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
            if (maxX < minX || maxY < minY) { box = default; return false; }
            box = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return true;
        }

        /// <summary>Crops the square out of the working render and scales it to the requested size with a bilinear blit, then composites the requested background under it when it is not transparent.</summary>
        static Texture2D CropScaleComposite(Texture2D working, SquareRegion square, int size, Color background)
        {
            int imageSize = working.width;
            var previous = RenderTexture.active;
            RenderTexture target = null;
            try
            {
                working.filterMode = FilterMode.Bilinear;
                working.wrapMode = TextureWrapMode.Clamp;
                target = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32);
                var scale = new Vector2(square.Side / imageSize, square.Side / imageSize);
                var offset = new Vector2(square.X / imageSize, square.Y / imageSize);
                Graphics.Blit(working, target, scale, offset);
                RenderTexture.active = target;
                var result = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                result.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                result.Apply();
                if (background.a > 0f) Composite(result, background);
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            }
        }

        /// <summary>Blends the requested background under the icon in place. A render against a transparent clear already leaves its colour weighted by its own alpha, so adding the background scaled by the leftover alpha keeps edges clean instead of the dark fringe a naive straight-alpha composite would leave.</summary>
        static void Composite(Texture2D icon, Color background)
        {
            Color32 bg = background;
            var pixels = icon.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                int inverse = 255 - p.a;
                pixels[i] = new Color32(
                    (byte)(p.r + bg.r * inverse / 255),
                    (byte)(p.g + bg.g * inverse / 255),
                    (byte)(p.b + bg.b * inverse / 255),
                    255);
            }
            icon.SetPixels32(pixels);
            icon.Apply();
        }

        /// <summary>
        /// Saves the texture as a PNG in the icons folder. An entry whose icon is already a PNG of this folder that
        /// no other entry holds gets that same file back (existingIcon), so capturing it again replaces its own
        /// picture. Every other save takes a file name nothing uses, so a second entry called Hat, a copy that
        /// still holds the first one's icon, or an avatar of the same name never replaces another picture.
        /// </summary>
        public static Texture2D Save(Texture2D texture, string avatarName, string plainEntryName, Texture2D existingIcon = null)
        {
            string folder = RootFolder + "/" + Safe(avatarName);
            EnsureFolder(RootFolder);
            EnsureFolder(folder);
            string path = OwnIconPath(existingIcon) ?? AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Safe(plainEntryName) + ".png");
            File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.maxTextureSize = 256;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// The path of an icon this tool saved earlier that only the entry being captured holds: a PNG inside the
        /// icons folder, held by at most one thing on the AvatarDocs of the open scenes. Null for no icon; for one
        /// that lives anywhere else, since a picture the creator brought is never overwritten; and for one another
        /// entry holds too (Duplicate and a copied component keep the icon), since a capture would replace that
        /// entry's picture as well.
        /// </summary>
        static string OwnIconPath(Texture2D icon)
        {
            if (icon == null) return null;
            var path = AssetDatabase.GetAssetPath(icon);
            bool ours = path.StartsWith(RootFolder + "/", StringComparison.Ordinal)
                && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
            return ours && HoldersOf(icon) <= 1 ? path : null;
        }

        /// <summary>How many things on the AvatarDocs of the open scenes hold this picture: an entry's icon, a component's submenu icon, a menu edit's icon or an installed menu asset's icon. The entry being captured is one of them.</summary>
        static int HoldersOf(Texture2D icon)
        {
            int holders = 0;
            foreach (var component in Object.FindObjectsOfType<AvatarDoc>(true))
            {
                if (component.menuIcon == icon) holders++;
                foreach (var entry in component.entries)
                    if (entry != null && entry.icon == icon) holders++;
                foreach (var edit in component.menuEdits)
                    if (edit != null && edit.icon == icon) holders++;
                foreach (var install in component.menuAssets)
                    if (install != null && install.icon == icon) holders++;
            }
            return holders;
        }

        /// <summary>Sets the entry's icon, with Undo. False, writing nothing, on a locked component or without an entry.</summary>
        public static bool Assign(AvatarDoc component, MenuEntry entry, Texture2D icon)
        {
            if (EntryEditing.Refuses(component) || entry == null) return false;
            Undo.RecordObject(component, "Capture icon");
            entry.icon = icon;
            EntryEditing.MarkChanged(component);
            return true;
        }

        /// <summary>A leaf name that cannot leave the icons folder: invalid characters become underscores, and leading or trailing dots and spaces go, so ".." and "../evil" cannot walk up.</summary>
        static string Safe(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = (name ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            string cleaned = new string(chars).Trim(' ', '.');
            return cleaned.Length == 0 ? "Icon" : cleaned;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
