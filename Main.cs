using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(LockerColorMod.Core), "✽ Locker Customization", "1.0.0", "Cassy")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace LockerColorMod
{
    public class Core : MelonMod
    {
        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("Locker Customization loaded! Look at a locker and press E to change its colour.");
            MelonEvents.OnGUI.Subscribe(LockerEditor.Draw);
        }

        public override void OnUpdate()
        {
            using (new Probe("Locker UpdateVisibility")) LockerEditor.UpdateVisibility();
            using (new Probe("Locker Reapply")) LockerEditor.TickPendingReapply();
            using (new Probe("Locker Clipboards")) ClipboardDuplicator.Tick();

            if (Input.GetKeyDown(KeyCode.E))
            {
                LockerEditor.TryInteract();
            }

            if (Input.GetKeyDown(KeyCode.Escape) && LockerEditor.Visible)
            {
                LockerEditor.TryClose();
            }
        }

        // Fires after a scene finishes loading (e.g. loading into a save) - arms a retry window
        // (drained by TickPendingReapply every frame) that restores any saved locker colour the
        // game's own save file doesn't preserve, even if lockers are still spawning in when this
        // fires, and resets ClipboardDuplicator's tracking so it re-duplicates onto the freshly
        // spawned lockers in the new scene.
        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            LockerEditor.OnSceneLoaded();
            ClipboardDuplicator.OnSceneLoaded();
        }
    }
}
