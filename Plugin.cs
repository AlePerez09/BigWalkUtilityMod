using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace BigWalkNoclip
{
    [BepInPlugin("com.ale.bigwalk.noclip", "Big Walk Noclip", "1.3.0")]
    public class Plugin : BasePlugin
    {
        internal static ConfigEntry<KeyCode> ToggleKey;
        internal static ConfigEntry<KeyCode> UpKey;
        internal static ConfigEntry<KeyCode> DownKey;
        internal static ConfigEntry<KeyCode> FastKey;
        internal static ConfigEntry<float> FlySpeed;
        internal static ConfigEntry<float> FastMultiplier;
        internal static ConfigEntry<bool> DisableController;
        internal static ConfigEntry<bool> DisableColliders;
        internal static ConfigEntry<bool> FreezeRigidbody;
        internal static ConfigEntry<float> AimRange;
        internal static ConfigEntry<KeyCode> InspectKey;
        internal static ConfigEntry<KeyCode> PressKey;
        internal static ConfigEntry<KeyCode> AutoPressKey;
        internal static ConfigEntry<float> AutoPressRange;
        internal static BepInEx.Logging.ManualLogSource Logger;

        public override void Load()
        {
            Logger = Log;
            ToggleKey = Config.Bind("Keys", "ToggleNoclip", KeyCode.F, "Toggle noclip fly on/off");
            UpKey = Config.Bind("Keys", "FlyUp", KeyCode.Space, "Fly up");
            DownKey = Config.Bind("Keys", "FlyDown", KeyCode.LeftControl, "Fly down");
            FastKey = Config.Bind("Keys", "FastKey", KeyCode.LeftShift, "Hold to fly faster");
            FlySpeed = Config.Bind("Fly", "Speed", 10f, "Base fly speed (units/sec)");
            FastMultiplier = Config.Bind("Fly", "FastMultiplier", 3f, "Speed multiplier while holding the fast key");
            DisableController = Config.Bind("Debug", "DisableCharacterController", true, "Disable the CharacterController while flying");
            DisableColliders = Config.Bind("Debug", "DisableColliders", true, "Make the player's colliders pass-through while flying");
            FreezeRigidbody = Config.Bind("Debug", "FreezeRigidbody", true, "Make the player's Rigidbody kinematic and gravity-free while flying");

            AimRange = Config.Bind("General", "AimRange", 8f, "How far away the inspect and press keys can reach (meters)");

            InspectKey = Config.Bind("Inspect", "InspectKey", KeyCode.H, "Aim at something and press this to log its scripts, fields and methods");

            PressKey = Config.Bind("Buttons", "PressKey", KeyCode.Y, "Aim at a button and press this to press it");
            AutoPressKey = Config.Bind("Buttons", "AutoPressKey", KeyCode.U, "Toggle auto-press: presses each button once when you walk within range");
            AutoPressRange = Config.Bind("Buttons", "AutoPressRange", 4f, "How close you must be for auto-press to press a button (meters)");

            ClassInjector.RegisterTypeInIl2Cpp<NoclipBehaviour>();
            AddComponent<NoclipBehaviour>();
            Log.LogInfo("Big Walk Noclip loaded. Press " + ToggleKey.Value + " to toggle.");
        }
    }

    public class NoclipBehaviour : MonoBehaviour
    {
        public NoclipBehaviour(IntPtr ptr) : base(ptr) { }

        private bool noclip;
        private Transform player;
        private CharacterController cc;
        private Rigidbody rb;
        private bool ccWasEnabled;
        private bool rbWasKinematic, rbHadGravity;
        private readonly List<Collider> disabledColliders = new List<Collider>();
        private float contTimer;
        private readonly List<Rigidbody> propBodies = new List<Rigidbody>();
        private bool autoPress;
        private float autoTimer;
        private readonly HashSet<int> pressedIds = new HashSet<int>();
        private static bool pressSigLogged;

        private void Update()
        {
            if (Input.GetKeyDown(Plugin.InspectKey.Value))
                InspectAimed();

            if (Input.GetKeyDown(Plugin.PressKey.Value))
                PressAimedButton();

            if (Input.GetKeyDown(Plugin.AutoPressKey.Value))
            {
                autoPress = !autoPress;
                pressedIds.Clear();
                Plugin.Logger.LogInfo("Auto-press " + (autoPress ? "ON" : "OFF"));
            }
            if (autoPress)
            {
                autoTimer -= Time.deltaTime;
                if (autoTimer <= 0f)
                {
                    autoTimer = 0.5f;
                    AutoPressNearby();
                }
            }

            if (Input.GetKeyDown(Plugin.ToggleKey.Value))
            {
                if (noclip) Disable(); else Enable();
            }
        }

        private void LateUpdate()
        {
            if (!noclip) return;
            if (player == null) { Disable(); return; }

            // Any collider we made pass-through that is no longer part of the player
            // (e.g. a dropped item) gets its solid collision back straight away.
            for (int i = disabledColliders.Count - 1; i >= 0; i--)
            {
                Collider dc = disabledColliders[i];
                if (dc == null) { disabledColliders.RemoveAt(i); continue; }
                if (!dc.transform.IsChildOf(player))
                {
                    dc.isTrigger = false;
                    disabledColliders.RemoveAt(i);
                }
            }

            Camera cam = Camera.main;
            Transform look = cam != null ? cam.transform : player;

            // Keep loose items from tunnelling through the ground while flying.
            contTimer -= Time.deltaTime;
            if (contTimer <= 0f)
            {
                contTimer = 0.5f;
                propBodies.Clear();
                foreach (Prop p in UnityEngine.Object.FindObjectsOfType<Prop>())
                {
                    if (p == null) continue;
                    Rigidbody prb = p.GetComponent<Rigidbody>();
                    if (prb == null) continue;
                    propBodies.Add(prb);
                    if (!prb.isKinematic)
                        prb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                }
            }

            foreach (Rigidbody b in propBodies)
            {
                if (b == null || b.isKinematic) continue;
                Vector3 v = b.velocity;
                if (v.sqrMagnitude > 35f * 35f) b.velocity = v.normalized * 35f;

                // If a falling loose item is right above solid ground, stop it there.
                if (v.y < -2f)
                {
                    RaycastHit gh;
                    if (Physics.Raycast(b.position, Vector3.down, out gh, 0.5f)
                        && gh.collider.attachedRigidbody != b)
                    {
                        b.position = gh.point + Vector3.up * 0.3f;
                        b.velocity = Vector3.zero;
                    }
                }
            }

            Vector3 dir = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) dir += look.forward;
            if (Input.GetKey(KeyCode.S)) dir -= look.forward;
            if (Input.GetKey(KeyCode.D)) dir += look.right;
            if (Input.GetKey(KeyCode.A)) dir -= look.right;
            if (Input.GetKey(Plugin.UpKey.Value)) dir += Vector3.up;
            if (Input.GetKey(Plugin.DownKey.Value)) dir -= Vector3.up;

            float speed = Plugin.FlySpeed.Value * (Input.GetKey(Plugin.FastKey.Value) ? Plugin.FastMultiplier.Value : 1f);
            if (dir.sqrMagnitude > 0f)
                player.position += dir.normalized * speed * Time.deltaTime;
        }

        private void Enable()
        {
            player = FindPlayer();
            if (player == null)
            {
                Plugin.Logger.LogWarning("Could not find the player.");
                return;
            }

            cc = player.GetComponentInChildren<CharacterController>();
            if (cc != null && Plugin.DisableController.Value) { ccWasEnabled = cc.enabled; cc.enabled = false; }
            else cc = null;

            // Pick the player's own rigidbody, never a held item's (items have a Prop script).
            rb = null;
            foreach (Rigidbody r in player.GetComponentsInChildren<Rigidbody>())
            {
                if (r.GetComponentInParent<Prop>() == null) { rb = r; break; }
            }
            if (rb != null && Plugin.FreezeRigidbody.Value)
            {
                rbWasKinematic = rb.isKinematic;
                rbHadGravity = rb.useGravity;
                rb.useGravity = false;
                rb.isKinematic = true;
            }
            else rb = null;

            disabledColliders.Clear();
            if (Plugin.DisableColliders.Value)
            {
                foreach (Collider c in player.GetComponentsInChildren<Collider>())
                {
                    // Make solid colliders triggers (disabling them breaks game scripts).
                    // Skip existing triggers, mesh colliders, and held items.
                    if (c.enabled && !c.isTrigger && c.TryCast<MeshCollider>() == null
                        && c.GetComponentInParent<Prop>() == null
                        && c.GetComponentInParent<PropKernal>() == null)
                    {
                        c.isTrigger = true;
                        disabledColliders.Add(c);
                    }
                }
            }

            noclip = true;
            Plugin.Logger.LogInfo("Noclip ON");
        }

        private void Disable()
        {
            noclip = false;
            foreach (Collider c in disabledColliders)
                if (c != null) c.isTrigger = false;
            disabledColliders.Clear();

            if (rb != null)
            {
                rb.isKinematic = rbWasKinematic;
                rb.useGravity = rbHadGravity;
            }
            if (cc != null) cc.enabled = ccWasEnabled;

            player = null; cc = null; rb = null;
            Plugin.Logger.LogInfo("Noclip OFF");
        }

        // Call the game's own PeckSwitch.Peck(...) on a button.
        private bool TryPeck(PeckSwitch sw)
        {
            try
            {
                var flags = System.Reflection.BindingFlags.Instance
                          | System.Reflection.BindingFlags.Public
                          | System.Reflection.BindingFlags.NonPublic;
                System.Reflection.MethodInfo noArg = null, playerArg = null;
                foreach (var m in sw.GetType().GetMethods(flags))
                {
                    if (m.Name != "Peck") continue;
                    var ps = m.GetParameters();
                    if (!pressSigLogged)
                    {
                        string sig = "";
                        foreach (var p in ps) sig += p.ParameterType.Name + " " + p.Name + ", ";
                        Plugin.Logger.LogInfo("PeckSwitch.Peck(" + sig + ")");
                    }
                    if (ps.Length == 0) noArg = m;
                    else if (ps.Length == 1 && ps[0].ParameterType.Name == "PlayerCharacter") playerArg = m;
                }
                pressSigLogged = true;

                if (noArg != null) { noArg.Invoke(sw, null); return true; }
                if (playerArg != null)
                {
                    PlayerCharacter pc = UnityEngine.Object.FindObjectOfType<PlayerCharacter>();
                    playerArg.Invoke(sw, new object[] { pc });
                    return true;
                }
                Plugin.Logger.LogWarning("No usable Peck overload found. See the PeckSwitch.Peck(...) lines above.");
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("Peck failed: " + e.Message);
            }
            return false;
        }

        private void PressAimedButton()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            RaycastHit hit;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out hit, Plugin.AimRange.Value))
            {
                Plugin.Logger.LogInfo("Press: nothing in range.");
                return;
            }

            PeckSwitch sw = hit.collider.GetComponentInParent<PeckSwitch>();
            if (sw == null)
            {
                Plugin.Logger.LogInfo("Press: '" + hit.collider.name + "' is not a PeckSwitch.");
                return;
            }

            Plugin.Logger.LogInfo("Pressing '" + sw.name + "'.");
            TryPeck(sw);
        }

        // Press each button once as you come within range; re-arms when you walk away.
        private void AutoPressNearby()
        {
            Camera cam = Camera.main;
            Vector3 p = cam != null ? cam.transform.position : transform.position;
            float r2 = Plugin.AutoPressRange.Value * Plugin.AutoPressRange.Value;

            foreach (PeckSwitch sw in UnityEngine.Object.FindObjectsOfType<PeckSwitch>())
            {
                if (sw == null) continue;
                int id = sw.GetInstanceID();
                bool near = (sw.transform.position - p).sqrMagnitude <= r2;
                if (near)
                {
                    if (pressedIds.Add(id))
                    {
                        Plugin.Logger.LogInfo("Auto-pressing '" + sw.name + "'.");
                        TryPeck(sw);
                    }
                }
                else pressedIds.Remove(id);
            }
        }

        // Log the game scripts (fields + methods) on whatever you're aiming at and its parents.
        private void InspectAimed()
        {
            Camera cam = Camera.main;
            if (cam == null) { Plugin.Logger.LogWarning("No main camera."); return; }

            RaycastHit hit;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out hit, Plugin.AimRange.Value))
            {
                Plugin.Logger.LogInfo("Inspect: nothing in range.");
                return;
            }

            var flags = Il2CppSystem.Reflection.BindingFlags.Instance
                      | Il2CppSystem.Reflection.BindingFlags.Public
                      | Il2CppSystem.Reflection.BindingFlags.NonPublic
                      | Il2CppSystem.Reflection.BindingFlags.DeclaredOnly;

            Transform cur = hit.collider.transform;
            Plugin.Logger.LogInfo("INSPECT hit '" + cur.name + "' at distance " + hit.distance);
            try
            {
                for (int level = 0; level < 4 && cur != null; level++)
                {
                    Plugin.Logger.LogInfo("-- object '" + cur.name + "' (level " + level + ")");
                    Plugin.Logger.LogInfo("   [state] layer=" + cur.gameObject.layer
                        + " active=" + cur.gameObject.activeSelf
                        + " parent=" + (cur.parent != null ? cur.parent.name : "none"));
                    Rigidbody irb = cur.GetComponent<Rigidbody>();
                    if (irb != null)
                        Plugin.Logger.LogInfo("   [state] rigidbody kinematic=" + irb.isKinematic
                            + " gravity=" + irb.useGravity
                            + " constraints=" + irb.constraints
                            + " detection=" + irb.collisionDetectionMode
                            + " velocity=" + irb.velocity);
                    foreach (Component comp in cur.GetComponents<Component>())
                    {
                        if (comp == null) continue;
                        var t = comp.GetIl2CppType();
                        string n = t.FullName;
                        Plugin.Logger.LogInfo("   component " + n);
                        if (n.StartsWith("UnityEngine.") || n.StartsWith("Mirror.")) continue;
                        foreach (var f in t.GetFields(flags))
                            Plugin.Logger.LogInfo("      field  " + f.FieldType.Name + " " + f.Name);
                        foreach (var m in t.GetMethods(flags))
                            Plugin.Logger.LogInfo("      method " + m.Name);
                    }
                    cur = cur.parent;
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("Inspect failed: " + e.Message);
            }
        }

        private Transform FindPlayer()
        {
            CharacterController c = UnityEngine.Object.FindObjectOfType<CharacterController>();
            if (c != null) return c.transform;

            Camera cam = Camera.main;
            if (cam != null)
            {
                Rigidbody r = cam.GetComponentInParent<Rigidbody>();
                if (r != null) return r.transform;
                return cam.transform.root;
            }
            return null;
        }
    }
}
