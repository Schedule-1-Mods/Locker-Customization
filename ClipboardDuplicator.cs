using System.Collections.Generic;
using MelonLoader;
using Il2CppScheduleOne.Employees;
using Il2CppTMPro;
using UnityEngine;

namespace LockerColorMod
{
    // Every locker's EmployeeHome exposes exactly one "Clipboard" (the mugshot + name sign) on
    // its front face. This clones that clipboard onto the locker's left and right faces too - the
    // front original is never touched - and keeps the clones showing the same photo/name as the
    // original for as long as the locker exists, including across employee reassignment.
    //
    // Runs automatically (no key press) against every locker already in the scene and any placed
    // later, on a periodic scan rather than every frame or via the game's own
    // onAssignedEmployeeChanged event - IL2CPP delegate/event marshalling has been the source of
    // several "stripped API" surprises elsewhere in these mods, so this favours a plain polling
    // loop that can't silently fail to fire.
    public static class ClipboardDuplicator
    {
        // An employee's name on either game branch: 0.4.7 (beta) has NPC.FullName, 0.4.6 (main)
        // has the field fullName instead. Each lookup sits in its own method so the one that
        // doesn't exist on this branch only fails when called (and is caught), rather than
        // breaking the method that uses it.
        private static bool? _hasFullName;
        private static string NameOf(Il2CppScheduleOne.NPCs.NPC npc)
        {
            if (npc == null) return "<none>";
            if (_hasFullName != false)
            {
                try { string n = FullNameBeta(npc); _hasFullName = true; return n; }
                catch (System.MissingMemberException) { _hasFullName = false; }
            }
            try { return FullNameMain(npc); } catch (System.MissingMemberException) { }
            return npc.gameObject.name;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static string FullNameBeta(Il2CppScheduleOne.NPCs.NPC npc) => npc.FullName;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static string FullNameMain(Il2CppScheduleOne.NPCs.NPC npc) => npc.FirstName + " " + npc.LastName;

        private const float ScanInterval = 5f; // a scene scan - new lockers get their side clipboards within 5 s
        private const float ResyncInterval = 1f;

        // How far the clone sits proud of the surface MeasureSide hits, in metres - negative pulls
        // it in slightly (past a visible gap) rather than floating just off the mesh.
        private const float SurfaceOffset = -0.012f;

        // Upper limit on how far a side clone may sit from the clipboard (metres).
        private const float MaxSideDistance = 1.6f;

        private sealed class SideClipboard
        {
            public GameObject Root;
            public SpriteRenderer Mugshot;
            public TextMeshPro NameLabel;
        }

        private sealed class LockerRecord
        {
            public EmployeeHome Home;
            public Vector3 FrontLocalPos;
            public float SideDistance;
            public List<SideClipboard> Sides = new List<SideClipboard>();
        }

        private static readonly Dictionary<int, LockerRecord> _records = new Dictionary<int, LockerRecord>();
        private static float _nextScanTime;
        private static float _nextResyncTime;

        // Walking every object in the scene for EmployeeHomes cost ~80 ms (a visible hitch every
        // 5 s with a big save). So the 5 s check is only a count of the items placed in the
        // properties you own; the full scan runs when that count changes, when a locker was seen
        // before its clipboard existed, and as a slow safety net.
        private const float SafetyScanInterval = 120f;
        private static int _lastSignature = int.MinValue;
        private static float _nextSafetyScan;
        private static bool _retryScan;
        private static int _retriesLeft = 6;

        public static void OnSceneLoaded()
        {
            _records.Clear();
            _nextScanTime = 0f;
            _nextResyncTime = 0f;
            _lastSignature = int.MinValue;
            _retryScan = false;
            _retriesLeft = 6;
        }

        // How many things are placed across the properties you own; -1 if the game won't say.
        private static int PlacedItemCount()
        {
            try
            {
                var owned = Il2CppScheduleOne.Property.Property.OwnedProperties;
                if (owned == null) return -1;
                int total = 0;
                for (int i = 0; i < owned.Count; i++)
                {
                    var p = owned[i];
                    if (p == null || p.BuildableItems == null) continue;
                    total += p.BuildableItems.Count + 1;
                }
                return total;
            }
            catch { return -1; }
        }

        public static void Tick()
        {
            if (Time.time >= _nextScanTime)
            {
                _nextScanTime = Time.time + ScanInterval;
                int signature = PlacedItemCount();
                if (signature < 0 || signature != _lastSignature || _retryScan || Time.time >= _nextSafetyScan)
                {
                    if (signature != _lastSignature) _retriesLeft = 6;
                    _lastSignature = signature;
                    _retryScan = false;
                    _nextSafetyScan = Time.time + SafetyScanInterval;
                    ScanForNewLockers();
                }
            }

            if (Time.time >= _nextResyncTime)
            {
                _nextResyncTime = Time.time + ResyncInterval;
                ResyncAll();
            }
        }

        // Cheap on purpose - FindObjectsOfType runs only once every ScanInterval seconds, and
        // skips every locker already recorded, so a large property costs nothing once its lockers
        // have all been duplicated.
        private static void ScanForNewLockers()
        {
            EmployeeHome[] homes = UnityEngine.Object.FindObjectsOfType<EmployeeHome>();
            foreach (EmployeeHome home in homes)
            {
                if (home == null)
                {
                    continue;
                }

                int id = home.GetInstanceID();
                if (_records.ContainsKey(id))
                {
                    continue;
                }

                // EmployeeHome turns out to sit on beds as well as lockers - the log showed this
                // mod duplicating clipboards onto "SingleBed(Clone)" objects, which is where the
                // wildly-off measurements and "meshed into the locker" look came from (a bed's
                // proportions are nothing like a locker's). The instantiated prefab's own name
                // reliably says which one it is (e.g. "Locker_Built(Clone)" vs "SingleBed(Clone)").
                if (home.gameObject == null || home.gameObject.name.IndexOf("Locker", System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    _records[id] = new LockerRecord { Home = home };
                    continue;
                }

                if (home.Clipboard == null)
                {
                    // A locker whose clipboard isn't there yet: look again in 5 s (a few times at most).
                    if (_retriesLeft > 0) { _retriesLeft--; _retryScan = true; }
                    continue;
                }

                try
                {
                    _records[id] = BuildRecord(home);
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Msg($"[ClipboardDuplicator] Failed to duplicate a locker's clipboard: {ex}");
                    // Recorded anyway (with no sides) so a broken locker isn't retried every scan.
                    _records[id] = new LockerRecord { Home = home };
                }
            }
        }

        private static LockerRecord BuildRecord(EmployeeHome home)
        {
            GameObject clipboard = home.Clipboard;
            Transform clipboardT = clipboard.transform;

            // home.transform turned out NOT to be this specific employee's own door position on
            // a multi-door bank - the diagnostic showed the clipboard sitting 0.32m off-centre
            // from it, meaning home.transform is some other shared reference point for the whole
            // bank. The clipboard's own position is the one point guaranteed to be this employee's
            // own spot (that's literally where their photo is), so side clones are now placed
            // relative to it directly instead.
            Transform anchor = clipboardT.parent != null ? clipboardT.parent : home.transform;
            Vector3 frontLocalPos = anchor.InverseTransformPoint(clipboardT.position);
            bool hasEmployee = home.AssignedEmployee != null;

            // The clipboard itself is mounted on the door, which swings open when the locker is
            // accessed - fine for the original (it's meant to swing with the door), but a side
            // clone parented under that same hierarchy (clipboardT.parent) swung open with it too,
            // dragging a sign that was positioned for the closed-door state along for the ride.
            // home.transform is the locker's own static placement root, which the door animates
            // relative to rather than being animated itself, so parenting the clones there instead
            // keeps them fixed regardless of what the door does.
            Transform staticParent = home.transform;

            // EmployeeHome.EmployeeSpecificMeshes is the renderer set the game's own role-colour
            // tinting uses - confirmed scoped to just this one employee's own door (colour changes
            // don't spill onto neighbours), unlike a broad GetComponentsInChildren scan from a
            // shared anchor, which is what measured a whole multi-door bank by mistake earlier.
            // Measuring its real bounds gives this door's actual shape instead of a guessed
            // constant: separate left/right extents (the clipboard isn't necessarily mounted
            // dead-centre on the door, so a single shared distance could overshoot one side into
            // open air - or a neighbour - while undershooting the other), plus how far the door's
            // own mesh extends front-to-back, so the clone can sit in the middle of the side panel
            // instead of right at the front edge where the clipboard itself is mounted.
            ComputeDoorBounds(home, clipboardT, out float rightExtent, out float leftExtent, out float centerForwardOffset);
            Vector3 depthCenterPos = clipboardT.position + clipboardT.forward * centerForwardOffset;

            // Find each side face for real: cast in from outside the locker towards it and put the
            // clone on the surface that's hit. No surface on a side (e.g. it's up against another
            // locker) means no clone there - never one hanging in the air.
            var root = home.GetComponentInParent<Il2CppScheduleOne.EntityFramework.BuildableItem>();
            Transform body = root != null ? root.transform : home.transform;
            float? rightDistance = MeasureSide(body, clipboardT, depthCenterPos, +1f);
            float? leftDistance = MeasureSide(body, clipboardT, depthCenterPos, -1f);
            MelonLogger.Msg($"[ClipboardDuplicator] {home.gameObject.name}: right side {(rightDistance.HasValue ? rightDistance.Value.ToString("0.000") + " m" : "none")}, " +
                            $"left side {(leftDistance.HasValue ? leftDistance.Value.ToString("0.000") + " m" : "none")} " +
                            $"(collider extents {rightExtent:0.000}/{leftExtent:0.000}, depth offset {centerForwardOffset:0.000}).");

            LockerRecord record = new LockerRecord { Home = home, FrontLocalPos = frontLocalPos, SideDistance = rightDistance ?? 0f };
            if (rightDistance.HasValue)
            {
                record.Sides.Add(BuildSide(home, staticParent, clipboardT, clipboard, depthCenterPos, rightDistance.Value, +1f, hasEmployee, "Clipboard_Right"));
            }

            if (leftDistance.HasValue)
            {
                record.Sides.Add(BuildSide(home, staticParent, clipboardT, clipboard, depthCenterPos, leftDistance.Value, -1f, hasEmployee, "Clipboard_Left"));
            }

            return record;
        }

        // Distance from the clipboard's centre line to the locker's own side surface on one side,
        // found by casting back in from 1.5 m out. Only hits on the locker itself count.
        private static float? MeasureSide(Transform body, Transform clipboardT, Vector3 center, float sign)
        {
            Vector3 dir = clipboardT.right * sign;
            Vector3 start = center + dir * (MaxSideDistance + 0.4f);
            float best = -1f;
            foreach (RaycastHit hit in Physics.RaycastAll(start, -dir, MaxSideDistance + 0.4f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == null || !hit.collider.transform.IsChildOf(body) || hit.collider.transform.IsChildOf(clipboardT))
                {
                    continue;
                }

                float fromCenter = (MaxSideDistance + 0.4f) - hit.distance;
                if (fromCenter > 0.02f && fromCenter > best)
                {
                    best = fromCenter;
                }
            }

            if (best < 0f || best > MaxSideDistance)
            {
                return null;
            }

            return best + SurfaceOffset;
        }

        // Projects each renderer's world-space bounds onto the clipboard's own right/forward axes,
        // so the result is "how far this door's own mesh extends" in directions relative to the
        // clipboard, regardless of the door's world rotation. rightExtent/leftExtent are measured
        // separately since the clipboard may not be mounted exactly centred on the door's width.
        private static void ComputeDoorBounds(EmployeeHome home, Transform clipboardT, out float rightExtent, out float leftExtent, out float centerForwardOffset)
        {
            rightExtent = 0f;
            leftExtent = 0f;
            float minForward = float.MaxValue;
            float maxForward = float.MinValue;
            bool found = false;

            // Measure the locker's actual solid body (its non-trigger colliders) rather than the
            // tinted door meshes: since the 0.4.7 update those meshes don't match the body any
            // more, which is what pushed the side clipboards out into thin air.
            var root = home.GetComponentInParent<Il2CppScheduleOne.EntityFramework.BuildableItem>();
            Transform body = root != null ? root.transform : home.transform;
            foreach (Collider c in body.GetComponentsInChildren<Collider>(true))
            {
                if (c == null || c.isTrigger || !c.enabled || c.transform.IsChildOf(clipboardT))
                {
                    continue;
                }

                Bounds b = c.bounds;
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    for (int sy = -1; sy <= 1; sy += 2)
                    {
                        for (int sz = -1; sz <= 1; sz += 2)
                        {
                            Vector3 corner = b.center + new Vector3(b.extents.x * sx, b.extents.y * sy, b.extents.z * sz);
                            Vector3 rel = corner - clipboardT.position;
                            float alongRight = Vector3.Dot(rel, clipboardT.right);
                            float alongForward = Vector3.Dot(rel, clipboardT.forward);

                            rightExtent = Mathf.Max(rightExtent, alongRight);
                            leftExtent = Mathf.Max(leftExtent, -alongRight);
                            minForward = Mathf.Min(minForward, alongForward);
                            maxForward = Mathf.Max(maxForward, alongForward);
                            found = true;
                        }
                    }
                }
            }

            if (found)
            {
                // A locker is well under a metre each side of its clipboard - anything more means
                // something else got measured, so don't let it fling the clone away.
                rightExtent = Mathf.Min(rightExtent, MaxSideDistance);
                leftExtent = Mathf.Min(leftExtent, MaxSideDistance);
                centerForwardOffset = Mathf.Clamp((minForward + maxForward) / 2f, -MaxSideDistance, MaxSideDistance);
                return;
            }

            var meshes = home.EmployeeSpecificMeshes;
            if (meshes != null)
            {
                for (int i = 0; i < meshes.Length; i++)
                {
                    MeshRenderer renderer = meshes[i];
                    if (renderer == null)
                    {
                        continue;
                    }

                    Bounds b = renderer.bounds;
                    for (int sx = -1; sx <= 1; sx += 2)
                    {
                        for (int sy = -1; sy <= 1; sy += 2)
                        {
                            for (int sz = -1; sz <= 1; sz += 2)
                            {
                                Vector3 corner = b.center + new Vector3(b.extents.x * sx, b.extents.y * sy, b.extents.z * sz);
                                Vector3 rel = corner - clipboardT.position;
                                float alongRight = Vector3.Dot(rel, clipboardT.right);
                                float alongForward = Vector3.Dot(rel, clipboardT.forward);

                                rightExtent = Mathf.Max(rightExtent, alongRight);
                                leftExtent = Mathf.Max(leftExtent, -alongRight);
                                minForward = Mathf.Min(minForward, alongForward);
                                maxForward = Mathf.Max(maxForward, alongForward);
                                found = true;
                            }
                        }
                    }
                }
            }

            centerForwardOffset = found ? (minForward + maxForward) / 2f : 0f;
        }

        private static SideClipboard BuildSide(EmployeeHome home, Transform anchor, Transform clipboardT, GameObject sourceClipboard, Vector3 depthCenterPos, float sideDistance, float sign, bool hasEmployee, string name)
        {
            GameObject clone = UnityEngine.Object.Instantiate(sourceClipboard, anchor);
            clone.name = name;

            // Computed in world space using the clipboard's own axes (Transform.right/forward/
            // rotation are always well-defined regardless of hierarchy) rather than trying to
            // reuse some other object's local space, which is what got the wrong reference point
            // last time. Starts from depthCenterPos (the door's own front-to-back centre) rather
            // than the clipboard's own position, so the clone sits in the middle of the side panel
            // instead of right at the front edge. If this ends up mirrored (right clone appears on
            // the left, or vice versa), swap which sign gets passed as +1f/-1f above.
            clone.transform.position = depthCenterPos + clipboardT.right * (sign * sideDistance);
            clone.transform.rotation = clipboardT.rotation * Quaternion.Euler(0f, sign * 90f, 0f);
            // Same size as the original, whatever the scale of the parent it now hangs under.
            Vector3 parentScale = anchor.lossyScale;
            clone.transform.localScale = new Vector3(
                clipboardT.lossyScale.x / Mathf.Max(parentScale.x, 0.0001f),
                clipboardT.lossyScale.y / Mathf.Max(parentScale.y, 0.0001f),
                clipboardT.lossyScale.z / Mathf.Max(parentScale.z, 0.0001f));

            // No employee assigned yet - the front clipboard just shows the game's own generic
            // "FirstName LastName" placeholder rather than being hidden, but the side copies
            // shouldn't exist at all until someone's actually assigned (ResyncAll flips this back
            // on the moment that happens).
            clone.SetActive(hasEmployee);

            // If the mugshot/name are already children of Clipboard, cloning it just brought them
            // along - GetComponentInChildren finds that copy. If they live elsewhere in the
            // locker's hierarchy instead, clone them too and re-parent under the new side clipboard
            // at the same offset they had from the original.
            SpriteRenderer mugshot = clone.GetComponentInChildren<SpriteRenderer>();
            if (mugshot == null && home.MugshotSprite != null)
            {
                mugshot = CloneSibling(home.MugshotSprite.gameObject, sourceClipboard.transform, clone.transform).GetComponent<SpriteRenderer>();
            }

            TextMeshPro nameLabel = clone.GetComponentInChildren<TextMeshPro>();
            if (nameLabel == null && home.NameLabel != null)
            {
                nameLabel = CloneSibling(home.NameLabel.gameObject, sourceClipboard.transform, clone.transform).GetComponent<TextMeshPro>();
            }

            return new SideClipboard { Root = clone, Mugshot = mugshot, NameLabel = nameLabel };
        }

        private static GameObject CloneSibling(GameObject source, Transform originalParent, Transform newParent)
        {
            Vector3 relPos = originalParent.InverseTransformPoint(source.transform.position);
            Quaternion relRot = Quaternion.Inverse(originalParent.rotation) * source.transform.rotation;

            GameObject clone = UnityEngine.Object.Instantiate(source, newParent);
            clone.transform.localPosition = relPos;
            clone.transform.localRotation = relRot;
            clone.transform.localScale = source.transform.localScale;
            clone.SetActive(true);
            return clone;
        }

        // Diagnostic only, tied to the exact locker LockerEditor's E-interact selects, so there's
        // no ambiguity about which log line belongs to which locker on screen.
        public static string DescribeState(EmployeeHome home)
        {
            if (home == null)
            {
                return "home is null";
            }

            if (!_records.TryGetValue(home.GetInstanceID(), out LockerRecord record))
            {
                return $"'{home.gameObject.name}' not yet processed by ClipboardDuplicator (scans every {ScanInterval:F1}s).";
            }

            string employeeName = home.AssignedEmployee != null ? NameOf(home.AssignedEmployee) : "<none>";
            string parentName = home.transform.parent != null ? home.transform.parent.name : "<none>";
            var sb = new System.Text.StringBuilder();
            sb.Append($"'{home.gameObject.name}' AssignedEmployee={employeeName} anchorWorldPos={home.transform.position} anchorParent='{parentName}' frontLocalPos={record.FrontLocalPos} measuredSideDistance={record.SideDistance:F3} recordedSides={record.Sides.Count}");
            foreach (SideClipboard side in record.Sides)
            {
                if (side.Root == null)
                {
                    sb.Append(" | <destroyed side>");
                    continue;
                }

                MeshRenderer[] sideRenderers = side.Root.GetComponentsInChildren<MeshRenderer>();
                int enabledRendererCount = 0;
                foreach (MeshRenderer r in sideRenderers)
                {
                    if (r != null && r.enabled)
                    {
                        enabledRendererCount++;
                    }
                }

                sb.Append($" | {side.Root.name} active={side.Root.activeSelf} localPos={side.Root.transform.localPosition} localForward={side.Root.transform.localRotation * Vector3.forward} rendererCount={sideRenderers.Length} enabledRenderers={enabledRendererCount} hasMugshot={side.Mugshot != null} mugshotEnabled={side.Mugshot != null && side.Mugshot.enabled} hasNameLabel={side.NameLabel != null}");
            }

            return sb.ToString();
        }

        // Keeps every side clone showing the same photo/name as the real, front-facing clipboard -
        // covers an employee being assigned, fired, or transferred after the clones were made.
        private static void ResyncAll()
        {
            foreach (LockerRecord record in _records.Values)
            {
                EmployeeHome home = record.Home;
                if (home == null || record.Sides.Count == 0)
                {
                    continue;
                }

                bool hasEmployee = home.AssignedEmployee != null;
                SpriteRenderer sourceMugshot = home.MugshotSprite;
                TextMeshPro sourceNameLabel = home.NameLabel;

                foreach (SideClipboard side in record.Sides)
                {
                    if (side.Root != null && side.Root.activeSelf != hasEmployee)
                    {
                        MelonLogger.Msg($"[ClipboardDuplicator] '{home.gameObject.name}' side visibility changing to {hasEmployee} (AssignedEmployee={(home.AssignedEmployee != null ? NameOf(home.AssignedEmployee) : "<none>")}).");
                        side.Root.SetActive(hasEmployee);
                    }

                    if (!hasEmployee)
                    {
                        continue;
                    }

                    if (side.Mugshot != null && sourceMugshot != null)
                    {
                        side.Mugshot.sprite = sourceMugshot.sprite;
                        side.Mugshot.color = sourceMugshot.color;
                        side.Mugshot.enabled = sourceMugshot.enabled;
                    }

                    if (side.NameLabel != null && sourceNameLabel != null)
                    {
                        side.NameLabel.text = sourceNameLabel.text;
                        side.NameLabel.color = sourceNameLabel.color;
                    }
                }
            }
        }
    }
}
