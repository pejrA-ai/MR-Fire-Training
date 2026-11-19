using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine.XR.Management;
#endif

namespace FireDrillMR
{
    [DefaultExecutionOrder(100)]
    public sealed class FireDrill : MonoBehaviour
    {
        public enum PressureSource { Simulated, External }

        [Header("Scene references")]
        [SerializeField] Camera desktopView, questView;
        [SerializeField] OVRCameraRig questRig;
        Camera view;
        [SerializeField] Transform rightController;
        [SerializeField] Transform station;
        [SerializeField] Transform[] flames;
        [SerializeField] Transform[] aimPoints;
        [SerializeField] GameObject editorRoom;
        [SerializeField] ParticleSystem spray;
        [SerializeField] LineRenderer rangeRing;
        [SerializeField] LineRenderer pointer;
        [SerializeField] Transform reticle;
        [SerializeField] Light fireLight;
        [SerializeField] DrillPanel panel;

        [Header("Pressure input")]
        [SerializeField] PressureSource pressureSource;
        [SerializeField, Range(.05f, .9f)] float minimumPressure = .22f;
        [SerializeField, Min(.1f)] float sensorTimeout = .5f;

        static readonly Color Orange = new Color32(255, 159, 79, 255);
        static readonly Color Mint = new Color32(115, 232, 197, 255);
        static readonly Color Pale = new Color32(199, 221, 223, 255);
        const float MaxReach = 4.25f;
        const float MinReach = .65f;
        const float SpreadRadius = .5f;

        float[] heat;
        Vector3[] originalScales;
        float externalPressure;
        float lastSample = float.NegativeInfinity;
        float simulatedPressure, elapsed, aimedTime;
        Vector2 desktopAim;
        bool placed, finished, spraying;
        public float FireRemaining { get; private set; }
        public float Pressure { get; private set; }
        public bool IsComplete => finished;
        bool useXr;
        float placementHintUntil;
#if UNITY_EDITOR
        XRManagerSettings editorXrManager;
#if UNITY_EDITOR_OSX
        const string SimulatorConfigKey = "META_XRSIM_CONFIG_JSON";
        string originalSimulatorConfig;
        string temporarySimulatorConfig;
#endif
#endif

        // Feed calibrated 0..1 samples on Unity's main thread, including when released.
        public void SubmitPressure(float normalizedPressure)
        {
            if (float.IsNaN(normalizedPressure) || float.IsInfinity(normalizedPressure)) return;
            externalPressure = Mathf.Clamp01(normalizedPressure);
            lastSample = Time.unscaledTime;
        }

        public void SetPressureSource(PressureSource source)
        {
            pressureSource = source;
            simulatedPressure = externalPressure = Pressure = 0;
            lastSample = float.NegativeInfinity;
        }

        void Awake()
        {
            heat = new float[flames.Length];
            originalScales = new Vector3[flames.Length];
            for (int i = 0; i < flames.Length; i++) originalScales[i] = flames[i].localScale;

            SetView(Application.platform == RuntimePlatform.Android);
        }

#if UNITY_EDITOR
        IEnumerator Start()
        {
            string runtime = Environment.GetEnvironmentVariable("XR_SELECTED_RUNTIME_JSON");
            if (string.IsNullOrEmpty(runtime))
                runtime = Environment.GetEnvironmentVariable("XR_RUNTIME_JSON");
            if (string.IsNullOrEmpty(runtime) ||
                !runtime.EndsWith("meta_openxr_simulator.json", StringComparison.OrdinalIgnoreCase))
                yield break;

            // OpenXR needs the graphics device to be ready before manual initialization.
            yield return null;
            var manager = XRGeneralSettings.Instance?.Manager;
            if (manager == null)
            {
                Debug.LogWarning("Fire Drill: enable the Standalone OpenXR loader for Meta XR Simulator.");
                yield break;
            }

            if (manager.activeLoader == null)
            {
#if UNITY_EDITOR_OSX
                UseCompatiblePassthroughTransport();
#endif
                InitializeEditorLoader(manager);
                if (manager.activeLoader == null)
                {
#if UNITY_EDITOR_OSX
                    RestoreSimulatorConfig();
#endif
                    Debug.LogWarning("Fire Drill: Meta XR Simulator did not initialize. Desktop preview is active.");
                    yield break;
                }
                editorXrManager = manager;
                manager.StartSubsystems();
            }

            SetView(true);
            Debug.Log("Fire Drill: Meta XR Simulator is using the Quest camera and passthrough.");
        }

        static void InitializeEditorLoader(XRManagerSettings manager)
        {
#if UNITY_EDITOR_OSX
            // Meta XR Core 205's optional Operator layer crashes in OpenXR pre-initialization
            // on this macOS setup. Keep other layers and restore the editor's settings afterward.
            const string layersKey = "XR_ENABLE_API_LAYERS";
            const string operatorLayer = "XR_APILAYER_METAX_operator";
            string previousLayers = Environment.GetEnvironmentVariable(layersKey);
            if (!string.IsNullOrEmpty(previousLayers))
            {
                string[] layers = previousLayers.Split(System.IO.Path.PathSeparator);
                layers = Array.FindAll(layers, layer => layer != operatorLayer);
                Environment.SetEnvironmentVariable(layersKey, string.Join(System.IO.Path.PathSeparator.ToString(), layers));
            }
            try { manager.InitializeLoaderSync(); }
            finally { Environment.SetEnvironmentVariable(layersKey, previousLayers); }
#else
            manager.InitializeLoaderSync();
#endif
        }

#if UNITY_EDITOR_OSX
        void UseCompatiblePassthroughTransport()
        {
            originalSimulatorConfig = Environment.GetEnvironmentVariable(SimulatorConfigKey);
            if (!System.IO.File.Exists(originalSimulatorConfig)) return;

            // The simulator reports shared-texture timeouts on this Mac. Meta documents
            // JPEG transport as its compatibility fallback for simulated passthrough.
            try
            {
                string config = System.IO.File.ReadAllText(originalSimulatorConfig);
                string compatibleConfig = config.Replace("\"gpu_handle\"", "\"jpg\"");
                if (compatibleConfig == config) return;
                temporarySimulatorConfig = System.IO.Path.Combine(Application.temporaryCachePath,
                    $"FireDrill-XRSim-{Guid.NewGuid():N}.json");
                System.IO.File.WriteAllText(temporarySimulatorConfig, compatibleConfig);
                Environment.SetEnvironmentVariable(SimulatorConfigKey, temporarySimulatorConfig);
            }
            catch (System.IO.IOException e)
            {
                Debug.LogWarning($"Fire Drill: could not prepare the simulator's passthrough configuration. {e.Message}");
            }
        }

        void RestoreSimulatorConfig()
        {
            if (temporarySimulatorConfig == null) return;
            if (Environment.GetEnvironmentVariable(SimulatorConfigKey) == temporarySimulatorConfig)
                Environment.SetEnvironmentVariable(SimulatorConfigKey, originalSimulatorConfig);
            try { System.IO.File.Delete(temporarySimulatorConfig); }
            catch (System.IO.IOException e) { Debug.LogWarning(e.Message); }
            temporarySimulatorConfig = null;
        }
#endif

        void OnDestroy()
        {
            // XR Plug-in Management still owns any loader it started itself.
            if (editorXrManager != null && editorXrManager.activeLoader != null)
            {
                questRig.gameObject.SetActive(false);
                editorXrManager.StopSubsystems();
                editorXrManager.DeinitializeLoader();
            }
#if UNITY_EDITOR_OSX
            RestoreSimulatorConfig();
#endif
        }
#endif

        void SetView(bool xr)
        {
            useXr = xr;
            view = xr ? questView : desktopView;
            panel.SetCamera(view);
            desktopView.gameObject.SetActive(!xr);
            editorRoom.SetActive(!xr);
            questRig.gameObject.SetActive(xr);
            view.backgroundColor = xr ? Color.clear : new Color(.025f, .05f, .07f, 1);
            placed = false;
            station.gameObject.SetActive(!xr);
            ResetDrill();
        }

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, .12f);
            Keyboard key = Keyboard.current;
            const OVRInput.Controller hand = OVRInput.Controller.RTouch;
            bool reset = useXr ? OVRInput.GetDown(OVRInput.Button.One, hand)
                : key != null && key.rKey.wasPressedThisFrame;
            bool tracking = !useXr || (OVRInput.GetControllerPositionTracked(hand)
                                       && OVRInput.GetControllerOrientationTracked(hand));
            if (useXr)
            {
                UpdateFloorPlacement(reset, tracking);
                if (!placed) return;
            }
            else if (reset) ResetDrill();

            if (!useXr && key != null)
            {
                Vector2 steer = new Vector2(
                    (key.rightArrowKey.isPressed ? 1 : 0) - (key.leftArrowKey.isPressed ? 1 : 0),
                    (key.upArrowKey.isPressed ? 1 : 0) - (key.downArrowKey.isPressed ? 1 : 0));
                desktopAim += steer * (1.05f * dt);
                desktopAim.x = Mathf.Clamp(desktopAim.x, -1.7f, 1.7f);
                desktopAim.y = Mathf.Clamp(desktopAim.y, -.25f, 1.4f);
            }

            float trigger = useXr ? OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, hand)
                : key != null && key.spaceKey.isPressed ? 1 : 0;
            bool hasSample = Time.unscaledTime - lastSample <= sensorTimeout;
            simulatedPressure = Mathf.MoveTowards(simulatedPressure, trigger, dt * (trigger > simulatedPressure ? 4 : 12));
            Pressure = pressureSource == PressureSource.External ? (hasSample ? externalPressure : 0) : simulatedPressure;

            Vector3 origin = useXr ? rightController.position + rightController.forward * .08f
                : view.transform.TransformPoint(new Vector3(.30f, -.27f, .40f));
            Vector3 direction = useXr ? rightController.forward
                : (station.TransformPoint(new Vector3(desktopAim.x, .14f + desktopAim.y, 0)) - origin).normalized;
            bool close = Vector3.Distance(origin, aimPoints[aimPoints.Length / 2].position) < MinReach;
            bool far = Vector3.Distance(origin, aimPoints[aimPoints.Length / 2].position) > MaxReach;
            bool active = !finished && tracking && Pressure >= minimumPressure;
            float strength = Mathf.InverseLerp(minimumPressure, 1, Pressure);
            bool target = false;
            float distance = MaxReach;
            if (!finished) elapsed += dt;

            for (int i = 0; i < aimPoints.Length; i++)
            {
                if (heat[i] <= 0) continue;
                Vector3 offset = aimPoints[i].position - origin;
                float along = Vector3.Dot(direction, offset);
                if (!tracking || close || far || along < MinReach || along > MaxReach) continue;
                float miss = (offset - direction * along).magnitude;
                if (miss >= SpreadRadius) continue;
                target = true;
                distance = Mathf.Min(distance, along);
                if (active)
                {
                    float effectiveness = Mathf.Lerp(1f, .38f, miss / SpreadRadius);
                    heat[i] = Mathf.Max(0, heat[i] - dt * strength * effectiveness * .57f);
                }
            }

            if (active && target) aimedTime += dt;
            UpdateFire();
            UpdateSpray(origin, direction, distance, active && !finished, strength);
            UpdatePointer(origin, direction, distance, tracking, target);
            string label = finished ? "Fire extinguished" : !tracking ? "Find your controller"
                : close ? "Step back" : far ? "Move within range"
                : active && target ? "Keep sweeping" : target ? "Squeeze to spray" : "Aim at the base";
            string help = finished ? $"DRILL TIME  {elapsed:00.0}s  /  ON TARGET  {aimedTime:00.0}s"
                : !tracking ? "Hold the right Touch controller." : close || far ? "Position yourself 1–4 m from the target."
                : active && target ? "Sweep across the remaining flames." : "Keep the nozzle low at the fire's base.";
            if (useXr && Time.unscaledTime < placementHintUntil)
                help = "Point at open floor 1–4 m away and press A.";
            string controls = useXr ? "A  PLACE ON FLOOR" : "ARROWS  AIM    SPACE  SPRAY    R  RESET";
            if (useXr && pressureSource == PressureSource.Simulated && !finished) controls = "TRIGGER  SPRAY    A  PLACE ON FLOOR";
            string source = pressureSource == PressureSource.External ? (hasSample ? "ESP32 / LIVE" : "ESP32 / WAITING")
                : useXr ? "TOUCH / SIMULATED" : "KEYBOARD / SIMULATED";
            panel.Refresh(label, help, controls, source, Pressure, FireRemaining, target, finished);
        }

        void UpdateFloorPlacement(bool relocate, bool controllerTracked)
        {
            if (!OVRManager.isHmdPresent || questRig.trackingSpace == null) return;
            float floorHeight = questRig.trackingSpace.position.y;
            Vector3 head = view.transform.position;
            if (head.y - floorHeight < .55f) return;

            Vector3 feet = new Vector3(head.x, floorHeight, head.z);
            Vector3 point;
            if (!placed)
            {
                Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up);
                if (forward.sqrMagnitude < .01f) return;
                point = feet + forward.normalized * 2.7f;
            }
            else if (relocate)
            {
                var floor = new Plane(Vector3.up, feet);
                var ray = new Ray(rightController.position, rightController.forward);
                if (!controllerTracked || !floor.Raycast(ray, out float distance))
                {
                    placementHintUntil = Time.unscaledTime + 3f;
                    return;
                }

                point = ray.GetPoint(distance);
                float groundDistance = Vector3.Distance(point, feet);
                if (groundDistance < 1f || groundDistance > 4f)
                {
                    placementHintUntil = Time.unscaledTime + 3f;
                    return;
                }
            }
            else return;

            station.SetPositionAndRotation(point, Quaternion.LookRotation(point - feet));
            station.gameObject.SetActive(true);
            placed = true;
            placementHintUntil = 0;
            ResetDrill();
        }

        void ResetDrill()
        {
            elapsed = aimedTime = simulatedPressure = Pressure = 0;
            desktopAim = Vector2.zero;
            finished = false;
            for (int i = 0; i < flames.Length; i++)
            {
                heat[i] = 1;
                flames[i].localScale = originalScales[i];
                flames[i].gameObject.SetActive(true);
                foreach (ParticleSystem ps in flames[i].GetComponentsInChildren<ParticleSystem>(true))
                {
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    if (!ps.main.playOnAwake) continue;
                    ps.Play(false);
                }
            }
            FireRemaining = 1;
            spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            spraying = false;
            rangeRing.startColor = rangeRing.endColor = Orange;
        }

        void UpdateFire()
        {
            float sum = 0;
            for (int i = 0; i < heat.Length; i++)
            {
                sum += heat[i];
                if (heat[i] <= 0)
                {
                    if (flames[i].gameObject.activeSelf) flames[i].gameObject.SetActive(false);
                }
                else flames[i].localScale = originalScales[i] * Mathf.Lerp(.20f, 1, Mathf.Sqrt(heat[i]));
            }
            FireRemaining = sum / heat.Length;
            finished = FireRemaining <= 0;
            rangeRing.startColor = rangeRing.endColor = finished ? Mint : Orange;
            if (fireLight != null)
                fireLight.intensity = FireRemaining * Mathf.Lerp(1.15f, 1.7f, Mathf.PerlinNoise(Time.time * 9, .25f));
        }

        void UpdateSpray(Vector3 origin, Vector3 direction, float distance, bool active, float strength)
        {
            spray.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
            var main = spray.main;
            main.startLifetime = Mathf.Clamp(distance / 7.5f, .16f, .54f);
            var emission = spray.emission;
            emission.rateOverTime = Mathf.Lerp(34, 130, strength);
            if (active == spraying) return;
            spraying = active;
            if (active) spray.Play();
            else spray.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void UpdatePointer(Vector3 origin, Vector3 direction, float distance, bool tracking, bool target)
        {
            pointer.enabled = tracking && !finished;
            reticle.gameObject.SetActive(pointer.enabled);
            if (!pointer.enabled) return;
            Vector3 tip = origin + direction * Mathf.Max(.3f, distance - .035f);
            pointer.SetPosition(0, origin + direction * .075f);
            pointer.SetPosition(1, tip);
            pointer.startColor = new Color(Pale.r, Pale.g, Pale.b, .20f);
            pointer.endColor = target ? Mint : Pale;
            reticle.position = tip;
            reticle.rotation = Quaternion.LookRotation(tip - view.transform.position);
        }
    }
}
