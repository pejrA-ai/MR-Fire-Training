using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace FireDrillMR
{
    public static class BuildFireDrill
    {
        public const string ScenePath = "Assets/FireDrill/FireDrill_MR.unity";
        const string Demo = "Assets/FireDrill/";
        const string FirePack = "Assets/AAA_fire_Unity5/";
        static readonly Color Ink = new Color32(14, 24, 30, 255);
        static readonly Color TextColor = new Color32(244, 244, 235, 255);
        static readonly Color Muted = new Color32(177, 201, 201, 255);
        static readonly Color Amber = new Color32(255, 164, 90, 255);
        static readonly Color Mint = new Color32(117, 236, 198, 255);
        static readonly Color Rule = new Color32(53, 74, 79, 255);

        // Batch entry point for rebuilding the authored scene.
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var desktop = new GameObject("Desktop preview camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            desktop.tag = "MainCamera";
            desktop.clearFlags = CameraClearFlags.SolidColor;
            desktop.backgroundColor = new Color(.025f, .05f, .07f, 1);
            desktop.nearClipPlane = .04f;
            desktop.farClipPlane = 35;
            desktop.fieldOfView = 52;
            Vector3 eye = new Vector3(-.12f, 1.62f, -.28f);
            desktop.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(new Vector3(.29f, .71f, 3) - eye));

            var rigPrefab = Load<GameObject>("Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab");
            var rig = ((GameObject)PrefabUtility.InstantiatePrefab(rigPrefab)).GetComponent<OVRCameraRig>();
            var manager = rig.GetComponent<OVRManager>() ?? rig.gameObject.AddComponent<OVRManager>();
            manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            manager.isInsightPassthroughEnabled = true;
            rig.gameObject.AddComponent<OVRPassthroughLayer>(); // Defaults to a background underlay.
            var quest = rig.transform.Find("TrackingSpace/CenterEyeAnchor").GetComponent<Camera>();
            quest.clearFlags = CameraClearFlags.SolidColor;
            quest.backgroundColor = Color.clear;
            quest.allowHDR = false;
            quest.nearClipPlane = .04f;
            if (rig.GetComponentInChildren<AudioListener>(true) == null) quest.gameObject.AddComponent<AudioListener>();
            rig.gameObject.SetActive(false);

            var station = New("Fire station (world locked)", null, new Vector3(0, 0, 3));
            var particlePrefab = Load<GameObject>(FirePack + "prefabs/style4/Fire_w_smoke.prefab");
            Transform[] flames = new Transform[3], targets = new Transform[3];
            for (int i = 0; i < flames.Length; i++)
            {
                float x = (i - 1) * .33f;
                var slot = New("Flame " + (i + 1), station, new Vector3(x, .035f, 0));
                slot.localScale = Vector3.one * (i == 1 ? .64f : .52f);
                var effect = (GameObject)PrefabUtility.InstantiatePrefab(particlePrefab, slot);
                PrefabUtility.UnpackPrefabInstance(effect, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                effect.transform.localPosition = Vector3.zero;
                effect.transform.localScale = Vector3.one;
                foreach (Transform t in effect.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                foreach (ParticleSystem ps in effect.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    var renderer = ps.GetComponent<ParticleSystemRenderer>();
                    if (renderer != null && (renderer.sharedMaterial == null || !renderer.sharedMaterial.shader.isSupported)
                        && !SystemInfo.graphicsDeviceType.Equals(GraphicsDeviceType.Null))
                        throw new InvalidOperationException("Unsupported Fire particle material: " + ps.name);
                }
                flames[i] = slot;
                targets[i] = New("Base target " + (i + 1), station, new Vector3(x, .14f, 0));
            }

            var lineMat = Material("Marker", "Sprites/Default", Color.white);
            var ring = Arc("Target / outer", station, .75f, 0, 360, .014f, lineMat, Amber);
            Arc("Target / inner", station, .67f, 0, 360, .006f, lineMat, new Color(.92f, .60f, .39f, .55f));
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45;
                Arc("Target index " + i, station, .815f, a - 1, a + 1, .013f, lineMat, Amber);
            }

            var pointer = NewLine("Nozzle ray", null, lineMat, .003f, Mint, true);
            pointer.positionCount = 2;
            pointer.enabled = false;
            var reticle = New("Nozzle reticle", null, new Vector3(0, .14f, 3));
            var reticleLine = NewLine("Reticle circle", reticle, lineMat, .008f, Mint);
            reticleLine.positionCount = 33;
            for (int i = 0; i < 33; i++)
            {
                float a = i * Mathf.PI / 16;
                reticleLine.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * .04f);
            }

            var light = New("Flame light", station, new Vector3(0, .43f, 0)).gameObject.AddComponent<Light>();
            light.type = LightType.Point; light.range = 2.4f; light.color = Amber; light.intensity = 1.6f;

            var spray = New("Extinguisher discharge", null, Vector3.zero).gameObject.AddComponent<ParticleSystem>();
            var mainSpray = spray.main;
            mainSpray.playOnAwake = false;
            mainSpray.loop = true;
            mainSpray.maxParticles = 140;
            mainSpray.startSpeed = new ParticleSystem.MinMaxCurve(6.5f, 8.5f);
            mainSpray.startSize = new ParticleSystem.MinMaxCurve(.10f, .22f);
            mainSpray.startLifetime = .42f;
            mainSpray.simulationSpace = ParticleSystemSimulationSpace.World;
            mainSpray.startColor = new Color(.85f, .95f, 1f, .69f);
            var shape = spray.shape;
            shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 5f; shape.radius = .018f;
            var fade = spray.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(.6f, 0), new GradientAlphaKey(.8f, .22f), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
            var size = spray.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .35f, 1, 1.6f));
            var sprayMat = Material("Dry powder", "FireDrill/Particle Alpha", Color.white);
            sprayMat.mainTexture = Load<Texture2D>(FirePack + "textures/l_particle.png");
            spray.GetComponent<ParticleSystemRenderer>().sharedMaterial = sprayMat;

            var room = PreviewRoom(lineMat);
            var panel = Panel(station, desktop);
            var drill = new GameObject("Fire drill controller").AddComponent<FireDrill>();
            Bind(drill, ("desktopView", desktop), ("questView", quest), ("questRig", rig),
                ("rightController", rig.rightControllerAnchor ?? rig.transform.Find("TrackingSpace/RightHandAnchor/RightControllerAnchor")),
                ("station", station), ("editorRoom", room), ("spray", spray),
                ("rangeRing", ring), ("pointer", pointer), ("reticle", reticle), ("fireLight", light), ("panel", panel));
            var state = new SerializedObject(drill);
            SetArray(state, "flames", flames);
            SetArray(state, "aimPoints", targets);
            state.ApplyModifiedPropertiesWithoutUndo();
            if (state.FindProperty("rightController").objectReferenceValue == null)
                throw new InvalidOperationException("OVR right controller anchor missing");

            var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (xr != null) { xr.InitManagerOnStart = false; EditorUtility.SetDirty(xr); }
            var config = OVRProjectConfig.CachedProjectConfig;
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Required;
            OVRProjectConfig.CommitProjectConfig(config);

            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Could not save demo scene");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }
                .Concat(EditorBuildSettings.scenes.Where(s => s.path != ScenePath)).ToArray();
            RepairLegacyPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log("FIRE DRILL SCENE READY: " + ScenePath);
        }

        static void RepairLegacyPrefab()
        {
            const string path = FirePack + "prefabs/style1/Fire_ball.prefab";
            var prefab = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int count = 0;
                foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
                    count += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                PrefabUtility.SaveAsPrefabAsset(prefab, path, out bool saved);
                if (!saved) throw new InvalidOperationException("Could not save upgraded Fire_ball");
                Debug.Log("Legacy Fire_ball saved successfully; missing scripts removed: " + count);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }

        static DrillPanel Panel(Transform station, Camera cam)
        {
            var go = new GameObject("Drill HUD", typeof(RectTransform), typeof(Canvas));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(station, false);
            rt.localPosition = new Vector3(1.28f, 1.14f, .10f);
            rt.localScale = Vector3.one * .00260f;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = cam;
            var panel = go.AddComponent<DrillPanel>();
            PopulatePanel(panel);
            return panel;
        }

        // Update the existing scene in place so its XR and training configuration is retained.
        public static void UpdatePanel()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var panel = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<DrillPanel>(true)).Single();
            var layout = (RectTransform)panel.transform;
            for (int child = layout.childCount - 1; child >= 0; child--)
                UnityEngine.Object.DestroyImmediate(layout.GetChild(child).gameObject);
            PopulatePanel(panel);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save the drill panel layout.");
        }

        static void PopulatePanel(DrillPanel panel)
        {
            var rt = (RectTransform)panel.transform;
            rt.sizeDelta = new Vector2(560, 372);
            var numericFont = Load<Font>(Demo + "Fonts/IBMPlexMono-Bold.ttf");

            Box(rt, "Panel", 0, 0, 560, 372, Ink);
            var statusMarker = Box(rt, "Status marker", 28, 30, 8, 8, Amber);
            var status = Label(rt, "Aim status", "ACQUIRE TARGET", 44, 20, 226, 28, 20, Amber, true);
            var source = Label(rt, "Input source", "KEYBOARD / SIMULATED", 274, 20, 258, 28, 18, Muted, false, true);

            var heading = Label(rt, "Prompt", "Aim at the base", 26, 62, 508, 62, 49, TextColor, true);
            var detail = Label(rt, "Hint", "Keep the nozzle low at the fire's base.", 28, 130, 504, 33, 23, Muted);
            Box(rt, "Readout rule", 28, 181, 504, 1, Rule);

            Label(rt, "Pressure label", "Nozzle pressure", 28, 195, 232, 25, 21, TextColor, true);
            Label(rt, "Heat label", "Fire remaining", 300, 195, 232, 25, 21, TextColor, true);
            var pressure = Label(rt, "Pressure reading", "00<size=28>%</size>", 25, 219, 235, 59, 58, Mint, font: numericFont);
            var fire = Label(rt, "Heat reading", "100<size=28>%</size>", 297, 219, 235, 59, 58, Amber, font: numericFont);
            var pressureFill = Meter(rt, "Pressure", 28, 288, Mint, 0);
            var fireFill = Meter(rt, "Heat", 300, 288, Amber, 1);

            Box(rt, "Controls background", 0, 316, 560, 56, new Color32(21, 36, 43, 255));
            var controls = Label(rt, "Controls", "<b>ARROWS</b>  Aim     <b>SPACE</b>  Spray     <b>R</b>  Reset",
                28, 328, 504, 32, 22, TextColor);
            Bind(panel, ("title", heading), ("detail", detail), ("controls", controls), ("source", source),
                ("pressureValue", pressure), ("fireValue", fire), ("state", status),
                ("pressureBar", pressureFill), ("fireBar", fireFill), ("statusStripe", statusMarker));
        }

        static Image Meter(RectTransform panel, string name, float x, float y, Color color, float value)
        {
            Box(panel, name + " track", x, y, 232, 8, Rule);
            var fill = Box(panel, name + " fill", x, y, 232, 8, color);
            fill.rectTransform.localScale = new Vector3(value, 1, 1);
            return fill;
        }

        static GameObject PreviewRoom(Material lineMat)
        {
            var room = New("Desktop training bay", null, Vector3.zero);
            var floor = Material("Bay floor", "Standard", new Color(.13f, .21f, .25f));
            var wall = Material("Bay wall", "Standard", new Color(.095f, .15f, .18f));
            var rib = Material("Bay trim", "Standard", new Color(.20f, .30f, .34f));
            Cube("Floor", room, new Vector3(0, -.09f, 3), new Vector3(12, .18f, 12), floor);
            Cube("Back wall", room, new Vector3(0, 1.7f, 5.1f), new Vector3(12, 3.4f, .14f), wall);
            for (int i = -4; i <= 4; i++)
            {
                Cube("Wall seam", room, new Vector3(i * 1.15f, 1.6f, 5.018f), new Vector3(.024f, 3.2f, .028f), rib);
                LineSegment("Floor mark", room, lineMat, new Vector3(i * .9f, .004f, -.5f),
                    new Vector3(i * .9f, .004f, 5), .007f, new Color(.44f, .62f, .63f, .24f));
            }
            for (int i = 0; i < 7; i++)
                LineSegment("Floor mark", room, lineMat, new Vector3(-4, .004f, -.5f + i * .85f),
                    new Vector3(4, .004f, -.5f + i * .85f), .007f, new Color(.44f, .62f, .63f, .24f));
            LineSegment("Back wall stripe", room, lineMat, new Vector3(-5, 1.66f, 5.012f),
                new Vector3(5, 1.66f, 5.012f), .017f, new Color(.35f, .68f, .66f, .46f));
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.44f, .53f, .62f);
            var fill = New("Bay fill", room, Vector3.zero).gameObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = .50f; fill.color = new Color(.71f, .87f, 1);
            fill.transform.rotation = Quaternion.Euler(52, -23, 0);
            return room.gameObject;
        }

        static void Cube(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name; cube.transform.SetParent(parent, false);
            cube.transform.localPosition = pos; cube.transform.localScale = scale;
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void LineSegment(string name, Transform parent, Material mat, Vector3 a, Vector3 b, float width, Color color)
        {
            var line = NewLine(name, parent, mat, width, color);
            line.positionCount = 2; line.SetPosition(0, a); line.SetPosition(1, b);
        }

        static LineRenderer Arc(string name, Transform parent, float radius, float from, float to, float width, Material mat, Color tint)
        {
            var line = NewLine(name, parent, mat, width, tint);
            line.positionCount = Mathf.Max(2, Mathf.RoundToInt((to - from) / 5) + 1);
            for (int i = 0; i < line.positionCount; i++)
            {
                float angle = Mathf.Lerp(from, to, (float)i / (line.positionCount - 1)) * Mathf.Deg2Rad;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, .018f, Mathf.Sin(angle) * radius));
            }
            return line;
        }

        static LineRenderer NewLine(string name, Transform parent, Material mat, float width, Color tint, bool world = false)
        {
            var line = New(name, parent, Vector3.zero).gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = world;
            line.sharedMaterial = mat;
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = tint;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        static Image Box(RectTransform parent, string name, float x, float y, float w, float h, Color color)
        {
            var rect = Element(parent, name, x, y, w, h);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = false;
            return image;
        }

        static Text Label(RectTransform parent, string name, string value, float x, float y, float w, float h,
            int size, Color color, bool bold = false, bool right = false, Font font = null)
        {
            var text = Element(parent, name, x, y, w, h).gameObject.AddComponent<Text>();
            text.font = font ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.fontSize = size; text.color = color;
            text.alignment = right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = value;
            return text;
        }

        static RectTransform Element(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0, 1);
            r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
            return r;
        }

        static Transform New(string name, Transform parent, Vector3 position)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false); t.localPosition = position;
            return t;
        }

        static Material Material(string name, string shaderName, Color color)
        {
            string path = Demo + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null) throw new InvalidOperationException("Missing shader " + shaderName);
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            if (mat.HasProperty("_Color")) mat.color = color;
            return mat;
        }

        static T Load<T>(string path) where T : UnityEngine.Object
        {
            return AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing " + path);
        }

        static void SetArray(SerializedObject obj, string field, Transform[] values)
        {
            var property = obj.FindProperty(field);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        static void Bind(UnityEngine.Object component, params (string field, UnityEngine.Object value)[] bindings)
        {
            var serialized = new SerializedObject(component);
            foreach (var (field, value) in bindings) serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
