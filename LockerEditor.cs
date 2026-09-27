using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using MelonLoader;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.PlayerScripts;
using UnityEngine;

namespace LockerColorMod
{
    // In-game GUI: look at a locker (an EmployeeHome - the assignable metal locker staff draw
    // their daily wage from and store items in) and press E to select it, then recolour it with a
    // small panel pinned to the left of the screen. Standalone mod - doesn't touch employees or
    // their appearance at all, just the lockers.
    public static class LockerEditor
    {
        private const float InteractRange = 5f;
        private const float CloseRange = 8f;
        private const float ContentWidth = 300f;
        private const int GradientSteps = 64;

        public static bool Visible;

        private static readonly Rect WindowRect = new Rect(20f, 20f, 340f, 300f);
        private static EmployeeHome _selected;
        private static string _selectedId;

        private static float _colourT;
        private static float _brightnessT = 1f;
        private static string _hex = "#FFFFFF";
        private static string _focusedField;
        private static string _selectAllField;

        // The game's own save file has no concept of a locker's colour at all, so a save/reload
        // discards any tint this mod applies. This is the mod's own side file (keyed by each
        // locker's persistent BuildableItem GUID) that remembers it and reapplies it, independent
        // of the game's save system.
        private static readonly Dictionary<string, string> _savedColours = new Dictionary<string, string>();
        private static bool _savedColoursLoaded;

        private static string GetDataFilePath()
        {
            string dllPath = Assembly.GetExecutingAssembly().Location;
            string modsDir = Path.GetDirectoryName(dllPath);
            return Path.Combine(modsDir, "LockerColorMod_Lockers.json");
        }

        private static void EnsureSavedColoursLoaded()
        {
            if (_savedColoursLoaded)
            {
                return;
            }

            _savedColoursLoaded = true;
            try
            {
                string path = GetDataFilePath();
                if (!File.Exists(path))
                {
                    return;
                }

                string json = File.ReadAllText(path);
                Dictionary<string, string> loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (loaded == null)
                {
                    return;
                }

                _savedColours.Clear();
                foreach (KeyValuePair<string, string> pair in loaded)
                {
                    _savedColours[pair.Key] = pair.Value;
                }

                MelonLogger.Msg($"[LockerEditor] Loaded {_savedColours.Count} saved locker colour(s) from {path}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Msg($"[LockerEditor] Failed to load saved locker colours: {ex}");
            }
        }

        private static void PersistSavedColours()
        {
            try
            {
                string json = JsonSerializer.Serialize(_savedColours, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GetDataFilePath(), json);
            }
            catch (System.Exception ex)
            {
                MelonLogger.Msg($"[LockerEditor] Failed to save locker colours: {ex}");
            }
        }

        // A locker's stable identity across saves/reloads is its BuildableItem GUID (the same
        // GUID the game's own save system uses) - EmployeeHome itself carries no ID, it's just the
        // component that happens to sit on the locker's root GameObject alongside that.
        private static string GetLockerId(EmployeeHome home)
        {
            if (home == null)
            {
                return null;
            }

            BuildableItem item = home.GetComponentInParent<BuildableItem>();
            return item != null ? item.GUID.ToString() : null;
        }

        private static IEnumerable<MeshRenderer> GetLockerRenderers(EmployeeHome home)
        {
            var specific = home.EmployeeSpecificMeshes;
            if (specific != null && specific.Length > 0)
            {
                for (int i = 0; i < specific.Length; i++)
                {
                    if (specific[i] != null)
                    {
                        yield return specific[i];
                    }
                }

                yield break;
            }

            // Fallback for any locker variant that doesn't wire up EmployeeSpecificMeshes -
            // recolour every mesh on it rather than silently doing nothing.
            foreach (MeshRenderer renderer in home.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer != null)
                {
                    yield return renderer;
                }
            }
        }

        // The locker's materials (SpecificMat_Default/Botanist/Chemist/Packager/Cleaner) are
        // shared assets swapped in whole by the game's own UpdateMaterial() whenever the assigned
        // employee's role changes - editing one directly would recolour every locker using that
        // asset. A MaterialPropertyBlock is a per-renderer override instead: it survives the game
        // swapping which shared material is assigned underneath it, without ever touching the
        // asset itself.
        private static string GetColourProperty(Material mat)
        {
            if (mat == null)
            {
                return null;
            }

            if (mat.HasProperty("_BaseColor"))
            {
                return "_BaseColor";
            }

            if (mat.HasProperty("_Color"))
            {
                return "_Color";
            }

            return null;
        }

        private static Color GetCurrentTint(EmployeeHome home)
        {
            foreach (MeshRenderer renderer in GetLockerRenderers(home))
            {
                string prop = GetColourProperty(renderer.sharedMaterial);
                if (prop == null)
                {
                    continue;
                }

                if (renderer.HasPropertyBlock())
                {
                    MaterialPropertyBlock block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block);
                    return block.GetColor(prop);
                }

                return renderer.sharedMaterial.GetColor(prop);
            }

            return Color.white;
        }

        private static void ApplyTint(EmployeeHome home, Color colour)
        {
            foreach (MeshRenderer renderer in GetLockerRenderers(home))
            {
                string prop = GetColourProperty(renderer.sharedMaterial);
                if (prop == null)
                {
                    continue;
                }

                MaterialPropertyBlock block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor(prop, colour);
                renderer.SetPropertyBlock(block);
            }
        }

        private static void ClearTint(EmployeeHome home)
        {
            foreach (MeshRenderer renderer in GetLockerRenderers(home))
            {
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                block.Clear();
                renderer.SetPropertyBlock(block);
            }
        }

        private static void SaveColourForLocker(string id, Color colour)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            EnsureSavedColoursLoaded();
            _savedColours[id] = ColourToHex(colour);
            PersistSavedColours();
        }

        private static void ClearSavedColourForLocker(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            EnsureSavedColoursLoaded();
            if (_savedColours.Remove(id))
            {
                PersistSavedColours();
            }
        }

        private const float ReapplyScanInterval = 0.5f;

        private static bool _pendingReapply;
        private static float _reapplyDeadline;
        private static float _nextReapplyScanTime;
        private static readonly HashSet<string> _reappliedLockerIds = new HashSet<string>();

        // Called once after a scene finishes loading (e.g. loading into a save). Placed lockers
        // can still be mid-spawn at this point, so this doesn't reapply directly - it arms a retry
        // window that TickPendingReapply drains until every saved locker is found.
        public static void OnSceneLoaded()
        {
            EnsureSavedColoursLoaded();
            _reappliedLockerIds.Clear();

            if (_savedColours.Count == 0)
            {
                _pendingReapply = false;
                return;
            }

            _pendingReapply = true;
            _reapplyDeadline = Time.time + 20f;
            _nextReapplyScanTime = 0f;
        }

        public static void TickPendingReapply()
        {
            if (!_pendingReapply)
            {
                return;
            }

            // FindObjectsOfType walks the whole loaded scene - calling it unthrottled from
            // OnUpdate (every single frame, for up to 20 seconds after every scene load) is what
            // caused the load-in stutter. A twice-a-second cadence still catches lockers within a
            // fraction of a second of spawning, at a small fraction of the cost.
            if (Time.time < _nextReapplyScanTime)
            {
                return;
            }

            _nextReapplyScanTime = Time.time + ReapplyScanInterval;

            EmployeeHome[] homes = UnityEngine.Object.FindObjectsOfType<EmployeeHome>();
            foreach (EmployeeHome home in homes)
            {
                if (home == null)
                {
                    continue;
                }

                string id = GetLockerId(home);
                if (id == null || _reappliedLockerIds.Contains(id) || !_savedColours.TryGetValue(id, out string hex))
                {
                    continue;
                }

                if (TryParseHex(hex, out Color colour))
                {
                    ApplyTint(home, colour);
                }

                _reappliedLockerIds.Add(id);
            }

            bool allDone = true;
            foreach (string id in _savedColours.Keys)
            {
                if (!_reappliedLockerIds.Contains(id))
                {
                    allDone = false;
                    break;
                }
            }

            if (allDone || Time.time > _reapplyDeadline)
            {
                if (_reappliedLockerIds.Count > 0)
                {
                    MelonLogger.Msg($"[LockerEditor] Reapplied saved colour to {_reappliedLockerIds.Count} locker(s) after scene load.");
                }

                _pendingReapply = false;
            }
        }

        // Called on E. Looks at whatever the player is aiming at; if it's a locker, selects it and
        // opens the panel. E only opens/switches the panel - it never closes it, so looking away
        // or pressing E again doesn't dismiss it. Closing is Escape (TryClose) or walking far
        // enough away (UpdateVisibility).
        public static void TryInteract()
        {
            EmployeeHome target = GetLookedAtLocker();
            if (target == null)
            {
                return;
            }

            SelectLocker(target);
            Visible = true;
        }

        public static void TryClose()
        {
            Visible = false;
        }

        public static void UpdateVisibility()
        {
            if (!Visible || _selected == null)
            {
                return;
            }

            Player player = Player.Local;
            if (player == null)
            {
                return;
            }

            if (Vector3.Distance(player.CameraPosition, _selected.transform.position) > CloseRange)
            {
                Visible = false;
            }
        }

        private static EmployeeHome GetLookedAtLocker()
        {
            Player player = Player.Local;
            if (player == null)
            {
                return null;
            }

            Vector3 origin = player.CameraPosition;
            Vector3 direction = player.CameraRotation * Vector3.forward;
            if (!Physics.Raycast(origin, direction, out RaycastHit hit, InteractRange))
            {
                return null;
            }

            EmployeeHome home = hit.collider != null ? hit.collider.GetComponentInParent<EmployeeHome>() : null;

            // EmployeeHome also sits on beds - employees are only ever assigned via a locker now,
            // so beds shouldn't open this panel at all (same fix ClipboardDuplicator needed).
            if (home == null || home.gameObject == null || home.gameObject.name.IndexOf("Locker", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            return home;
        }

        private static void SelectLocker(EmployeeHome home)
        {
            _selected = home;
            _selectedId = GetLockerId(home);
            _focusedField = null;

            LogFootprintDiagnostics(home);
            MelonLogger.Msg($"[LockerEditor] ClipboardDuplicator state for this exact locker: {ClipboardDuplicator.DescribeState(home)}");

            EnsureSavedColoursLoaded();
            Color current = _selectedId != null && _savedColours.TryGetValue(_selectedId, out string hex) && TryParseHex(hex, out Color saved)
                ? saved
                : GetCurrentTint(home);

            DecomposeColour(current, out _colourT, out _brightnessT);
            _hex = ColourToHex(current);
        }

        // Diagnostic only, for scoping whether shrinking this locker's grid footprint is even
        // possible - GridItem.FootprintX/Y are whole tile counts (there's no fractional/"half
        // tile" concept anywhere in this game's placement grid), so this just reports what the
        // locker currently occupies. Logged once per E-interact rather than continuously.
        private static void LogFootprintDiagnostics(EmployeeHome home)
        {
            try
            {
                GridItem gridItem = home.GetComponentInParent<GridItem>();
                if (gridItem == null)
                {
                    MelonLogger.Msg("[LockerEditor] Footprint diagnostic: this locker has no GridItem component.");
                    return;
                }

                int tileCount = gridItem.CoordinateFootprintTilePairs != null ? gridItem.CoordinateFootprintTilePairs.Count : -1;

                MelonLogger.Msg($"[LockerEditor] Footprint diagnostic: FootprintX={gridItem.FootprintX} FootprintY={gridItem.FootprintY} footprintTileCount={tileCount} gridTileSize={Il2CppScheduleOne.Tiles.Grid.TileSize:F3}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Msg($"[LockerEditor] Footprint diagnostic threw: {ex}");
            }
        }

        public static void Draw()
        {
            if (!Visible || _selected == null)
            {
                return;
            }

            GUI.Window(848211, WindowRect, (GUI.WindowFunction)DrawWindow, "Locker Colour");
        }

        private static void DrawWindow(int id)
        {
            if (_selected == null)
            {
                GUILayout.Label("Locker unavailable.");
                return;
            }

            DrawSectionTitle("Locker Colour");

            GUILayout.Label("Colour");
            _colourT = DrawDraggableGradientBar(GetSpectrumTexture(), _colourT, SpectrumTToColour);

            GUILayout.Space(8f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Brightness");
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{Mathf.RoundToInt(_brightnessT * 100f)}%", GetBrightnessPercentStyle());
            GUILayout.EndHorizontal();
            _brightnessT = DrawDraggableGradientBar(GetBrightnessTexture(), _brightnessT, v => Color.Lerp(Color.black, Color.white, v));

            GUILayout.Space(6f);

            Color colour = ComposeColour(_colourT, _brightnessT);
            string liveHex = ColourToHex(colour);

            GUILayout.BeginHorizontal();
            GUILayout.Space(40f);
            DrawShapePreview(GetLockerIconStyle(), colour, 70f, 70f);

            GUILayout.BeginVertical();
            GUILayout.Space(24f);
            string typed = DrawEditableField("lockerHex", _hex, 7, 90f);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            if (typed != _hex)
            {
                _hex = typed;
                if (TryParseHex(_hex, out Color parsed))
                {
                    DecomposeColour(parsed, out _colourT, out _brightnessT);
                }
            }
            else if (_focusedField != "lockerHex" && _hex != liveHex)
            {
                _hex = liveHex;
            }

            GUILayout.Space(14f);
            if (GUILayout.Button("Apply to Locker", GUILayout.Width(ContentWidth), GUILayout.Height(26f)))
            {
                Color applied = ComposeColour(_colourT, _brightnessT);
                ApplyTint(_selected, applied);
                SaveColourForLocker(_selectedId, applied);
            }

            GUILayout.Space(6f);
            if (GUILayout.Button("Restore to Default", GUILayout.Width(ContentWidth), GUILayout.Height(24f)))
            {
                ClearTint(_selected);
                ClearSavedColourForLocker(_selectedId);

                Color restored = GetCurrentTint(_selected);
                DecomposeColour(restored, out _colourT, out _brightnessT);
                _hex = ColourToHex(restored);
            }
        }

        // --- Colour math, drawn widgets and hand-rolled text entry below use the same
        // stripped-IMGUI-call workarounds MelonLoader mods for this game generally need
        // (GUI.DrawTexture and GUI.DoTextField throw "Method unstripping failed" in this build) ---

        private static Texture2D _spectrumTexture;
        private static Texture2D _brightnessTexture;
        private static Texture2D _circleTexture;
        private static GUIStyle _circleStyle;
        private static Texture2D _solidTexture;
        private static GUIStyle _solidStyle;
        private static GUIStyle _headerStyle;
        private static GUIStyle _sectionTitleTextStyle;
        private static GUIStyle _brightnessPercentStyle;
        private static GUIStyle _inputBoxStyle;
        private static bool _draggableBarSupported = true;

        private static void DrawSectionTitle(string title)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0.12f, 0.12f, 0.12f, 0.9f);
            GUILayout.BeginVertical(GetHeaderStyle(), GUILayout.Width(ContentWidth));
            GUI.color = previous;
            GUILayout.Label(title.ToUpperInvariant(), GetSectionTitleTextStyle());
            GUILayout.EndVertical();
        }

        private static GUIStyle GetSectionTitleTextStyle()
        {
            if (_sectionTitleTextStyle == null)
            {
                _sectionTitleTextStyle = new GUIStyle(GUI.skin.label);
                _sectionTitleTextStyle.fontSize = 16;
                _sectionTitleTextStyle.fontStyle = FontStyle.Bold;
                _sectionTitleTextStyle.alignment = TextAnchor.MiddleCenter;
                _sectionTitleTextStyle.normal.textColor = Color.white;
            }

            return _sectionTitleTextStyle;
        }

        private static GUIStyle GetBrightnessPercentStyle()
        {
            if (_brightnessPercentStyle == null)
            {
                _brightnessPercentStyle = new GUIStyle(GUI.skin.label);
                _brightnessPercentStyle.fontStyle = FontStyle.Bold;
                _brightnessPercentStyle.normal.textColor = new Color(0.95f, 0.75f, 0.25f);
            }

            return _brightnessPercentStyle;
        }

        private static GUIStyle GetHeaderStyle()
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle();
                _headerStyle.padding = new RectOffset(6, 6, 4, 4);
                _headerStyle.normal.background = GetSolidTexture();
            }

            return _headerStyle;
        }

        private static GUIStyle GetInputBoxStyle()
        {
            if (_inputBoxStyle == null)
            {
                _inputBoxStyle = new GUIStyle(GUI.skin.textField);
                _inputBoxStyle.normal.textColor = Color.white;
                _inputBoxStyle.hover.textColor = Color.white;
                _inputBoxStyle.active.textColor = Color.white;
                _inputBoxStyle.focused.textColor = Color.white;
            }

            return _inputBoxStyle;
        }

        private static Rect DrawGradientBar(Texture2D texture)
        {
            const float height = 12f;
            Rect r = GUILayoutUtility.GetRect(ContentWidth, height, GUILayout.Width(ContentWidth), GUILayout.Height(height));
            GUIStyle style = new GUIStyle();
            style.normal.background = texture;
            GUI.Box(r, GUIContent.none, style);
            return r;
        }

        private static void DrawColourMarker(Rect barRect, float t, Color tint)
        {
            const float size = 16f;
            float cx = barRect.x + t * barRect.width;
            float cy = barRect.y + barRect.height / 2f;
            Rect circleRect = new Rect(cx - size / 2f, cy - size / 2f, size, size);

            Color previous = GUI.color;
            GUI.color = tint;
            GUI.Box(circleRect, GUIContent.none, GetCircleStyle());
            GUI.color = previous;
        }

        private static float DrawDraggableGradientBar(Texture2D texture, float t, System.Func<float, Color> tintAt)
        {
            Rect barRect = DrawGradientBar(texture);

            if (_draggableBarSupported)
            {
                try
                {
                    const float handleSize = 16f;
                    Rect hitRect = new Rect(barRect.x, barRect.y + barRect.height / 2f - handleSize / 2f, barRect.width, handleSize);
                    int controlId = GUIUtility.GetControlID(FocusType.Passive);
                    Event e = Event.current;

                    if (e.type == EventType.MouseDown && hitRect.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = controlId;
                        t = Mathf.Clamp01((e.mousePosition.x - barRect.x) / barRect.width);
                        e.Use();
                    }
                    else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == controlId)
                    {
                        t = Mathf.Clamp01((e.mousePosition.x - barRect.x) / barRect.width);
                        e.Use();
                    }
                    else if (e.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }

                    DrawColourMarker(barRect, t, tintAt(t));
                    return t;
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Msg($"[LockerEditor] Draggable gradient bar unsupported, falling back to a plain slider: {ex}");
                    _draggableBarSupported = false;
                }
            }

            DrawColourMarker(barRect, t, tintAt(t));
            return GUILayout.HorizontalSlider(t, 0f, 1f, GUILayout.Width(ContentWidth));
        }

        private static Texture2D GetCircleTexture()
        {
            if (_circleTexture != null)
            {
                return _circleTexture;
            }

            const int size = 20;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float radius = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - radius + 0.5f;
                    float dy = y - radius + 0.5f;
                    bool inside = dx * dx + dy * dy <= radius * radius;
                    tex.SetPixel(x, y, inside ? Color.white : new Color(0f, 0f, 0f, 0f));
                }
            }

            tex.Apply();
            _circleTexture = tex;
            return _circleTexture;
        }

        private static GUIStyle GetCircleStyle()
        {
            if (_circleStyle == null)
            {
                _circleStyle = new GUIStyle();
                _circleStyle.normal.background = GetCircleTexture();
            }

            return _circleStyle;
        }

        private static void DrawShapePreview(GUIStyle style, Color colour, float width, float height)
        {
            Rect r = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
            Color previous = GUI.color;
            GUI.color = colour;
            GUI.Box(r, GUIContent.none, style);
            GUI.color = previous;
        }

        private static Texture2D GetSolidTexture()
        {
            if (_solidTexture != null)
            {
                return _solidTexture;
            }

            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _solidTexture = tex;
            return _solidTexture;
        }

        private static GUIStyle GetSolidStyle()
        {
            if (_solidStyle == null)
            {
                _solidStyle = new GUIStyle();
                _solidStyle.normal.background = GetSolidTexture();
            }

            return _solidStyle;
        }

        // The colour swatch: the embedded locker icon if it'll load, otherwise a plain square
        // (same fallback approach EmployeeEditor uses for its cosmetic icons - ImageConversion.
        // LoadImage is another API that may be stripped in this build, so this is only ever tried
        // once per session and wrapped in a try/catch).
        private static Texture2D _lockerIconTexture;
        private static bool _lockerIconLoadAttempted;
        private static GUIStyle _lockerIconStyle;

        private static GUIStyle GetLockerIconStyle()
        {
            if (_lockerIconStyle == null)
            {
                if (!_lockerIconLoadAttempted)
                {
                    _lockerIconLoadAttempted = true;
                    _lockerIconTexture = TryLoadEmbeddedImage("LockerColorMod.Assets.locker_icon.png");
                }

                _lockerIconStyle = new GUIStyle();
                _lockerIconStyle.normal.background = _lockerIconTexture != null ? _lockerIconTexture : GetSolidTexture();
            }

            return _lockerIconStyle;
        }

        private static Texture2D TryLoadEmbeddedImage(string resourceName)
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using Stream stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    return null;
                }

                byte[] bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);

                Texture2D tex = new Texture2D(2, 2);
                if (!ImageConversion.LoadImage(tex, bytes))
                {
                    return null;
                }

                ConvertToWhiteMask(tex);
                return tex;
            }
            catch
            {
                return null;
            }
        }

        // GUI tints a texture by multiplying it with GUI.color, so only pixels that start out
        // white can become any chosen colour. This turns every "foreground" pixel fully white
        // (keeping only its shape) and makes the background fully transparent, so the whole icon -
        // not just its outline - tints correctly to any selected colour. If the source PNG already
        // has real alpha transparency, that's trusted directly; otherwise the background colour is
        // sampled from the image's own corners.
        private static void ConvertToWhiteMask(Texture2D tex)
        {
            bool hasRealAlpha = false;
            for (int y = 0; y < tex.height && !hasRealAlpha; y++)
            {
                for (int x = 0; x < tex.width; x++)
                {
                    if (tex.GetPixel(x, y).a < 0.95f)
                    {
                        hasRealAlpha = true;
                        break;
                    }
                }
            }

            if (hasRealAlpha)
            {
                for (int y = 0; y < tex.height; y++)
                {
                    for (int x = 0; x < tex.width; x++)
                    {
                        Color c = tex.GetPixel(x, y);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, c.a));
                    }
                }
            }
            else
            {
                Color bg = AverageCorners(tex);
                const float threshold = 0.18f;

                for (int y = 0; y < tex.height; y++)
                {
                    for (int x = 0; x < tex.width; x++)
                    {
                        Color c = tex.GetPixel(x, y);
                        float dist = Mathf.Abs(c.r - bg.r) + Mathf.Abs(c.g - bg.g) + Mathf.Abs(c.b - bg.b);
                        bool isBackground = dist < threshold;
                        tex.SetPixel(x, y, isBackground ? new Color(1f, 1f, 1f, 0f) : new Color(1f, 1f, 1f, 1f));
                    }
                }
            }

            tex.Apply();
        }

        private static Color AverageCorners(Texture2D tex)
        {
            int w = tex.width - 1;
            int h = tex.height - 1;
            Color c0 = tex.GetPixel(0, 0);
            Color c1 = tex.GetPixel(w, 0);
            Color c2 = tex.GetPixel(0, h);
            Color c3 = tex.GetPixel(w, h);
            return new Color((c0.r + c1.r + c2.r + c3.r) / 4f, (c0.g + c1.g + c2.g + c3.g) / 4f, (c0.b + c1.b + c2.b + c3.b) / 4f);
        }

        private static Texture2D GetSpectrumTexture()
        {
            if (_spectrumTexture == null)
            {
                _spectrumTexture = BuildGradient(SpectrumTToColour);
            }

            return _spectrumTexture;
        }

        private static Texture2D GetBrightnessTexture()
        {
            if (_brightnessTexture == null)
            {
                _brightnessTexture = BuildGradient(t => Color.Lerp(Color.black, Color.white, t));
            }

            return _brightnessTexture;
        }

        private static Texture2D BuildGradient(System.Func<float, Color> colourAt)
        {
            Texture2D tex = new Texture2D(GradientSteps, 1);
            for (int x = 0; x < GradientSteps; x++)
            {
                float t = (float)x / (GradientSteps - 1);
                tex.SetPixel(x, 0, colourAt(t));
            }

            tex.Apply();
            return tex;
        }

        private const float WhiteCapWidth = 0.06f;
        private const float GreyCapWidth = 0.06f;
        private const float BlackCapWidth = 0.06f;
        private const float BrownCapWidth = 0.06f;
        private const float CapsWidth = WhiteCapWidth + GreyCapWidth + BlackCapWidth + BrownCapWidth;
        private static readonly Color GreyColour = new Color32(141, 141, 141, 255);
        private static readonly Color BrownColour = new Color32(114, 75, 53, 255);

        private static Color SpectrumTToColour(float t)
        {
            if (t <= WhiteCapWidth)
            {
                return Color.white;
            }

            if (t <= WhiteCapWidth + GreyCapWidth)
            {
                return GreyColour;
            }

            if (t <= WhiteCapWidth + GreyCapWidth + BlackCapWidth)
            {
                return Color.black;
            }

            if (t <= CapsWidth)
            {
                return BrownColour;
            }

            float hue = (t - CapsWidth) / (1f - CapsWidth);
            return Color.HSVToRGB(hue, 1f, 1f);
        }

        private static float ColourToSpectrumT(Color colour)
        {
            float max = Mathf.Max(colour.r, Mathf.Max(colour.g, colour.b));
            float min = Mathf.Min(colour.r, Mathf.Min(colour.g, colour.b));
            float saturation = max <= 0f ? 0f : (max - min) / max;

            if (saturation < 0.08f)
            {
                float lightness = (max + min) / 2f;
                if (lightness > 0.85f)
                {
                    return WhiteCapWidth / 2f;
                }

                if (lightness < 0.15f)
                {
                    return WhiteCapWidth + GreyCapWidth + BlackCapWidth / 2f;
                }

                return WhiteCapWidth + GreyCapWidth / 2f;
            }

            float brownDistance = Mathf.Abs(colour.r - BrownColour.r) + Mathf.Abs(colour.g - BrownColour.g) + Mathf.Abs(colour.b - BrownColour.b);
            if (brownDistance < 0.3f)
            {
                return WhiteCapWidth + GreyCapWidth + BlackCapWidth + BrownCapWidth / 2f;
            }

            Color.RGBToHSV(colour, out float hue, out _, out _);
            return CapsWidth + hue * (1f - CapsWidth);
        }

        private static Color ComposeColour(float colourT, float brightnessT)
        {
            Color baseColour = SpectrumTToColour(colourT);
            return new Color(baseColour.r * brightnessT, baseColour.g * brightnessT, baseColour.b * brightnessT, 1f);
        }

        private static void DecomposeColour(Color colour, out float colourT, out float brightnessT)
        {
            float value = Mathf.Max(colour.r, Mathf.Max(colour.g, colour.b));
            if (value <= 0.0001f)
            {
                colourT = ColourToSpectrumT(Color.black);
                brightnessT = 1f;
                return;
            }

            Color fullBrightness = new Color(colour.r / value, colour.g / value, colour.b / value);
            colourT = ColourToSpectrumT(fullBrightness);
            brightnessT = Mathf.Clamp01(value);
        }

        private static string ColourToHex(Color colour)
        {
            Color32 c = colour;
            return $"#{c.r:X2}{c.g:X2}{c.b:X2}";
        }

        private static bool TryParseHex(string text, out Color colour)
        {
            colour = Color.white;
            string t = text.TrimStart('#');
            if (t.Length != 6)
            {
                return false;
            }

            if (!byte.TryParse(t.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) ||
                !byte.TryParse(t.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) ||
                !byte.TryParse(t.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            {
                return false;
            }

            colour = new Color32(r, g, b, 255);
            return true;
        }

        // Reads/writes the Windows clipboard directly via user32/kernel32 (GUIUtility's own
        // systemCopyBuffer getter is stripped in this build, same class of issue as the other
        // "obscure" Unity API calls this file works around).
        private const uint ClipboardFormatUnicodeText = 13;
        private const uint GlobalMemMoveableZeroed = 0x0042;

        [DllImport("user32.dll")]
        private static extern bool OpenClipboard(System.IntPtr hWndNewOwner);

        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll")]
        private static extern System.IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll")]
        private static extern System.IntPtr SetClipboardData(uint uFormat, System.IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern System.IntPtr GlobalAlloc(uint uFlags, System.UIntPtr dwBytes);

        [DllImport("kernel32.dll")]
        private static extern System.IntPtr GlobalLock(System.IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(System.IntPtr hMem);

        private static void TrySetClipboard(string text)
        {
            try
            {
                if (!OpenClipboard(System.IntPtr.Zero))
                {
                    return;
                }

                try
                {
                    EmptyClipboard();

                    int byteCount = (text.Length + 1) * 2;
                    System.IntPtr hGlobal = GlobalAlloc(GlobalMemMoveableZeroed, (System.UIntPtr)byteCount);
                    if (hGlobal == System.IntPtr.Zero)
                    {
                        return;
                    }

                    System.IntPtr target = GlobalLock(hGlobal);
                    if (target == System.IntPtr.Zero)
                    {
                        return;
                    }

                    Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                    GlobalUnlock(hGlobal);

                    SetClipboardData(ClipboardFormatUnicodeText, hGlobal);
                }
                finally
                {
                    CloseClipboard();
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Msg($"[LockerEditor] Copy threw: {ex}");
            }
        }

        private static string TryGetClipboard()
        {
            try
            {
                if (!OpenClipboard(System.IntPtr.Zero))
                {
                    return null;
                }

                try
                {
                    System.IntPtr handle = GetClipboardData(ClipboardFormatUnicodeText);
                    if (handle == System.IntPtr.Zero)
                    {
                        return null;
                    }

                    System.IntPtr pointer = GlobalLock(handle);
                    if (pointer == System.IntPtr.Zero)
                    {
                        return null;
                    }

                    try
                    {
                        return Marshal.PtrToStringUni(pointer);
                    }
                    finally
                    {
                        GlobalUnlock(handle);
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Msg($"[LockerEditor] Paste threw: {ex}");
                return null;
            }
        }

        // A minimal, self-drawn text field (GUILayout.TextField is unusable here since
        // GUI.DoTextField is stripped in this game's IL2CPP build).
        private static string DrawEditableField(string fieldId, string value, int maxLength, float width)
        {
            bool isFocused = _focusedField == fieldId;
            bool isSelectedAll = isFocused && _selectAllField == fieldId;
            string display = isFocused ? value + "|" : value;

            Color previousBg = GUI.backgroundColor;
            if (isSelectedAll)
            {
                GUI.backgroundColor = new Color(0.3f, 0.5f, 1f, 1f);
            }

            bool clicked = GUILayout.Button(display, GetInputBoxStyle(), GUILayout.Width(width), GUILayout.Height(20f));
            GUI.backgroundColor = previousBg;

            if (clicked)
            {
                bool wasFocused = isFocused;
                _focusedField = wasFocused ? null : fieldId;
                isFocused = !wasFocused;
                _selectAllField = isFocused ? fieldId : null;
            }

            if (!isFocused)
            {
                return value;
            }

            Event evt = Event.current;
            if (evt.type != EventType.KeyDown)
            {
                return value;
            }

            bool selectedAll = _selectAllField == fieldId;

            if (evt.control && evt.keyCode == KeyCode.A)
            {
                _selectAllField = fieldId;
            }
            else if (evt.control && evt.keyCode == KeyCode.C)
            {
                TrySetClipboard(value);
            }
            else if (evt.control && evt.keyCode == KeyCode.V)
            {
                string pasted = TryGetClipboard();
                if (!string.IsNullOrEmpty(pasted))
                {
                    value = pasted.Length > maxLength ? pasted.Substring(0, maxLength) : pasted;
                }

                _selectAllField = null;
            }
            else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter || evt.keyCode == KeyCode.Escape)
            {
                _focusedField = null;
                _selectAllField = null;
            }
            else if (evt.keyCode == KeyCode.Backspace)
            {
                value = selectedAll ? string.Empty : (value.Length > 0 ? value.Substring(0, value.Length - 1) : value);
                _selectAllField = null;
            }
            else if (evt.character != '\0' && !char.IsControl(evt.character))
            {
                value = selectedAll ? string.Empty : value;
                if (value.Length < maxLength)
                {
                    value += evt.character;
                }

                _selectAllField = null;
            }

            evt.Use();
            return value;
        }
    }
}
