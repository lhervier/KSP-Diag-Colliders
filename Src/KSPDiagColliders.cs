using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace com.github.lhervier.ksp.diag.colliders
{
    /// <summary>
    /// Collider viewer. In flight, draws the colliders within a few hundred metres of the active vessel:
    /// those of the ground (stock gives a terrain quad its own mesh as collider) as the grey outline of
    /// their cells and, as lines of a colour of their own, the edges of their triangles and the colliders of
    /// one layer, chosen in a small window (Local Scenery, the layer of the statics, by default): the
    /// colliders of the statics on that layer (anything placed by a PQSCity or a PQSCity2: the buildings and
    /// runway of the space centre, Kerbal Konstructs groups), pale blue when switched off, and any other
    /// collider of that layer that is on, but for the parts of the active vessel. By default what stands in
    /// front of the drawing hides it, so
    /// that a collider standing proud of a surface shows and one beneath it does not. The window switches
    /// between that, drawn through everything, and nothing, and lists the colliders drawn, each with its
    /// colour, whether it is off, convex or unreadable, and a box that stops drawing it. Nothing is written to
    /// the log.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class KSPDiagColliders : MonoBehaviour
    {
        private const string LOG_PREFIX = "[KSPDiagColliders] ";

        // Unity's own shader for its debug lines: unlike the unlit shaders of the game, it lets a script
        // choose the depth test.
        private const string SHADER_NAME = "Hidden/Internal-Colored";

        // Distance from the active vessel, in metres, within which colliders are drawn.
        private const float RADIUS = 300f;

        // How often the colliders are looked at again, in seconds of real time. The drawing itself
        // follows them every frame.
        private const float REFRESH_PERIOD = 0.25f;

        // How often the statics of the body are listed again, in seconds of real time.
        private const float SCAN_PERIOD = 2f;

        // Layer the drawing is queued on: Local Scenery, the layer of the terrain and of the statics,
        // which the flight cameras draw.
        private const int DRAW_LAYER = 15;

        private static readonly Color GROUND_LINE = new Color(0.3f, 0.3f, 0.3f, 1f);
        private static readonly Color DISABLED_LINE = new Color(0.6f, 0.8f, 1f, 0.6f);

        // Colours of the colliders of statics that are on, one per collider while there are enough: hues far
        // apart from one another, and from the grey of the ground and the pale blue of the colliders off.
        private static readonly Color[] PALETTE =
        {
            new Color(0.1f, 0.45f, 1f, 1f),   // blue
            new Color(1f, 0.5f, 0f, 1f),      // orange
            new Color(1f, 0f, 1f, 1f),        // magenta
            new Color(0f, 1f, 1f, 1f),        // cyan
            new Color(1f, 0.9f, 0f, 1f),      // yellow
            new Color(1f, 0.15f, 0.15f, 1f),  // red
            new Color(0.4f, 1f, 0f, 1f),      // lime
            new Color(0.6f, 0.3f, 1f, 1f),    // purple
            new Color(1f, 0.55f, 0.75f, 1f),  // pink
            new Color(0f, 0.6f, 0.45f, 1f),   // teal
        };

        // Names of the colours of PALETTE, in the same order, and of the pale blue of a collider switched off.
        private static readonly string[] PALETTE_NAMES =
        {
            "blue", "orange", "magenta", "cyan", "yellow", "red", "lime", "purple", "pink", "teal",
        };
        private const string DISABLED_NAME = "pale blue";

        // Height of the list of colliders in the window, in pixels, beyond which it scrolls.
        private const float LIST_HEIGHT = 300f;

        // Drawn after everything else; when drawn through everything, the colliders on come last so that
        // nothing covers them.
        private const int GROUND_QUEUE = 4000;
        private const int DISABLED_QUEUE = 4001;
        private const int ACTIVE_QUEUE = 4002;

        private Material groundLine;
        private Material[] activeLines;
        private Material disabledLine;

        // The window: its id, distinct from those of the other Diags, its width, and its gap from the
        // right and top edges of the screen, where the windows of Diag TerrainHeight and Diag FloatingOrigin do not open.
        private const int WINDOW_ID = 0x47485005;
        private const float WINDOW_WIDTH = 360f;
        private const float WINDOW_MARGIN = 60f;

        // Labels of the display modes in the window, in the order of Display.
        private static readonly string[] DISPLAY_LABELS =
        {
            "Hidden by what stands in front",
            "Through everything",
            "Off",
        };

        /// <summary>What is drawn, and how.</summary>
        private enum Display
        {
            DepthTested,
            ThroughEverything,
            Nothing,
        }

        // Static, so that the choice survives a scene change or a reload.
        private static Display display = Display.DepthTested;

        // Layer whose colliders are drawn, besides the terrain: Local Scenery by default, the layer of the
        // statics. Static for the same reason.
        private static int layer = 15;

        private CelestialBody body;
        private PQS sphere;
        private readonly Dictionary<PQ, GroundOverlay> ground = new Dictionary<PQ, GroundOverlay>();
        // The colliders drawn in colour: those of the ground quads, whatever the layer, and those of the
        // chosen layer.
        private readonly Dictionary<Collider, ColliderOverlay> overlays = new Dictionary<Collider, ColliderOverlay>();

        // Every collider of the statics of the body, as last listed.
        private readonly List<Collider> staticColliders = new List<Collider>();

        private float nextRefresh;
        private float nextScan;

        // Scratch buffers of a refresh.
        private readonly HashSet<PQ> nearQuads = new HashSet<PQ>();
        private readonly HashSet<Collider> nearColliders = new HashSet<Collider>();
        private readonly List<Collider> found = new List<Collider>();

        // How many colliders are drawn in colour, on and switched off, for the window.
        private int drawnActive;
        private int drawnDisabled;

        private Rect windowRect;

        // The colliders drawn in colour, in the order of the window's list, and where that list is
        // scrolled to.
        private readonly List<ColliderOverlay> listed = new List<ColliderOverlay>();
        private Vector2 listScroll;

        // Colliders unticked in the window's list: not drawn.
        private readonly HashSet<Collider> hidden = new HashSet<Collider>();

        private void Awake()
        {
            Shader shader = Shader.Find(SHADER_NAME);
            if (shader == null)
            {
                Debug.LogError(LOG_PREFIX + "shader " + SHADER_NAME + " not found: nothing will be drawn");
                enabled = false;
                return;
            }
            groundLine = MakeMaterial(shader, GROUND_LINE, GROUND_QUEUE);
            activeLines = new Material[PALETTE.Length];
            for (int i = 0; i < PALETTE.Length; i++)
            {
                activeLines[i] = MakeMaterial(shader, PALETTE[i], ACTIVE_QUEUE);
            }
            disabledLine = MakeMaterial(shader, DISABLED_LINE, DISABLED_QUEUE);
            ApplyDepthTest();
            windowRect = new Rect(Screen.width - WINDOW_WIDTH - WINDOW_MARGIN, WINDOW_MARGIN, WINDOW_WIDTH, 0f);

            // Shader.Find answers with whatever carries the name: without a depth test of its own, the
            // three modes draw alike.
            if (!groundLine.HasProperty("_ZTest"))
            {
                Debug.LogWarning(LOG_PREFIX + "shader " + shader.name + " has no _ZTest: every mode draws the same");
            }
        }

        private void OnDestroy()
        {
            Clear();

            // None of them exists when the shader was not found.
            foreach (Material material in AllMaterials())
            {
                if (material != null)
                {
                    Destroy(material);
                }
            }
        }

        /// <summary>A transparent material of <paramref name="color"/>, drawn at
        /// <paramref name="queue"/>.</summary>
        private static Material MakeMaterial(Shader shader, Color color, int queue)
        {
            Material material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetColor("_Color", color);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.renderQueue = queue;
            return material;
        }

        /// <summary>Gives every material the depth test of the current display mode.</summary>
        private void ApplyDepthTest()
        {
            CompareFunction test = display == Display.ThroughEverything
                ? CompareFunction.Always
                : CompareFunction.LessEqual;
            foreach (Material material in AllMaterials())
            {
                material.SetInt("_ZTest", (int)test);
            }
        }

        private IEnumerable<Material> AllMaterials()
        {
            yield return groundLine;
            yield return disabledLine;
            if (activeLines != null)
            {
                foreach (Material material in activeLines)
                {
                    yield return material;
                }
            }
        }

        private void OnGUI()
        {
            if (!windowVisible)
            {
                return;
            }
            GUI.skin = HighLogic.Skin;
            windowRect = GUILayout.Window(WINDOW_ID, windowRect, DrawWindow, "KSP Diag - Colliders");
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.Label(Heading() + ":");
            ChooseDisplay(GUILayout.SelectionGrid((int)display, DISPLAY_LABELS, 1));
            GUILayout.BeginHorizontal();
            // The name takes all the width the buttons leave, so that they stay at the right edge whatever
            // its length, under the pointer of a user clicking through the layers.
            GUILayout.Label("Layer " + layer + " (" + LayerMask.LayerToName(layer) + ")", GUILayout.ExpandWidth(true));
            int chosenLayer = layer;
            if (GUILayout.Button("<", GUILayout.Width(30f)))
            {
                chosenLayer = (layer + 31) % 32;
            }
            if (GUILayout.Button(">", GUILayout.Width(30f)))
            {
                chosenLayer = (layer + 1) % 32;
            }
            GUILayout.EndHorizontal();
            if (chosenLayer != layer)
            {
                layer = chosenLayer;
                nextRefresh = 0f;
            }

            GUILayout.Label(Counts());

            listScroll = GUILayout.BeginScrollView(listScroll, GUILayout.Height(LIST_HEIGHT));
            foreach (ColliderOverlay overlay in listed)
            {
                Collider collider = overlay.Collider;
                if (collider == null)
                {
                    continue;
                }
                GUILayout.BeginHorizontal();
                // A swatch of the colour the collider is drawn in.
                Rect swatch = GUILayoutUtility.GetRect(16f, 16f, GUILayout.Width(16f), GUILayout.Height(16f));
                Color previous = GUI.color;
                GUI.color = ColorOf(overlay);
                GUI.DrawTexture(swatch, Texture2D.whiteTexture);
                GUI.color = previous;
                // Unticking a collider stops drawing it; it stays in the list so that it can be ticked again.
                bool shown = !hidden.Contains(collider);
                if (GUILayout.Toggle(shown, ShortName(collider) + Remarks(overlay)) != shown)
                {
                    if (shown)
                    {
                        hidden.Add(collider);
                    }
                    else
                    {
                        hidden.Remove(collider);
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        /// <summary>
        /// What the window shows, ready to be written as JSON: the body (empty when there is none), the
        /// radius, the mode, the layer and its name, how many ground quads and colliders in colour are
        /// drawn, and the list of the colliders, each with its name as listed, its colour, whether it is on,
        /// convex or unreadable, the level of its quad when it is ground, and whether it is ticked.
        /// </summary>
        internal Dictionary<string, object> Read()
        {
            List<object> colliders = new List<object>();
            foreach (ColliderOverlay overlay in listed)
            {
                Collider collider = overlay.Collider;
                if (collider == null)
                {
                    continue;
                }
                Dictionary<string, object> line = new Dictionary<string, object>
                {
                    { "name", ShortName(collider) },
                    { "colour", ColorNameOf(overlay) },
                    { "on", collider.enabled },
                    { "convex", IsConvex(collider) },
                    { "unreadable", IsUnreadable(overlay) },
                    { "ticked", !hidden.Contains(collider) }
                };
                PQ quad = QuadOf(collider);
                if (quad != null)
                {
                    line.Add("groundLevel", quad.subdivision);
                }
                colliders.Add(line);
            }
            return new Dictionary<string, object>
            {
                { "body", sphere != null && body != null ? body.bodyName : "" },
                { "radius", (double)RADIUS },
                { "display", DISPLAY_LABELS[(int)display] },
                { "layer", layer },
                { "layerName", LayerMask.LayerToName(layer) },
                { "groundQuads", ground.Count },
                { "collidersOn", drawnActive },
                { "collidersOff", drawnDisabled },
                { "colliders", colliders }
            };
        }

        /// <summary>The first line of the window: the radius, and the body when there is one.</summary>
        private string Heading()
        {
            return "Colliders within " + RADIUS.ToString("F0") + " m"
                + (sphere != null && body != null ? " on " + body.bodyName : "");
        }

        /// <summary>The line of the window that counts what is drawn.</summary>
        private string Counts()
        {
            return ground.Count + " ground quad(s), " + drawnActive + " collider(s) on, " + drawnDisabled
                + " switched off";
        }

        /// <summary>What the list of the window says after the name of the collider of
        /// <paramref name="overlay"/>: the level of its quad when it is ground, whether it is off, and what is
        /// drawn of it otherwise than the physics has it.</summary>
        private static string Remarks(ColliderOverlay overlay)
        {
            PQ quad = QuadOf(overlay.Collider);
            return (quad != null ? " (ground, level " + quad.subdivision + ")" : "")
                + (overlay.Collider.enabled ? "" : " (off)")
                + (IsConvex(overlay.Collider) ? " (convex)" : "")
                + (IsUnreadable(overlay) ? " (unreadable mesh)" : "");
        }

        /// <summary>The terrain quad whose collider <paramref name="collider"/> is, or null when it is not
        /// ground.</summary>
        private static PQ QuadOf(Collider collider)
        {
            return collider != null ? collider.GetComponent<PQ>() : null;
        }

        /// <summary>Whether <paramref name="collider"/> is a convex mesh: the physics uses its hull, not the
        /// mesh drawn.</summary>
        private static bool IsConvex(Collider collider)
        {
            MeshCollider meshCollider = collider as MeshCollider;
            return meshCollider != null && meshCollider.convex;
        }

        /// <summary>Whether the mesh of the collider of <paramref name="overlay"/> cannot be read by scripts:
        /// the box of its bounds is drawn instead.</summary>
        private static bool IsUnreadable(ColliderOverlay overlay)
        {
            return overlay.Collider is MeshCollider && overlay.Unreadable;
        }

        /// <summary>The colour the collider of <paramref name="overlay"/> is drawn in.</summary>
        private static Color ColorOf(ColliderOverlay overlay)
        {
            return overlay.Collider.enabled ? PALETTE[overlay.ColorIndex] : DISABLED_LINE;
        }

        /// <summary>The name of the colour the collider of <paramref name="overlay"/> is drawn in.</summary>
        private static string ColorNameOf(ColliderOverlay overlay)
        {
            return overlay.Collider.enabled ? PALETTE_NAMES[overlay.ColorIndex] : DISABLED_NAME;
        }

        /// <summary>
        /// Chooses a display mode, as a click on its label in the window does: its index in the window, from
        /// the top. Returns the label of the mode now chosen.
        /// </summary>
        internal string ChooseDisplay(int index)
        {
            if (index < 0 || index >= DISPLAY_LABELS.Length)
            {
                throw new System.ArgumentOutOfRangeException(nameof(index), "0 to " + (DISPLAY_LABELS.Length - 1));
            }
            Display chosen = (Display)index;
            if (chosen != display)
            {
                display = chosen;
                ApplyDepthTest();
            }
            return DISPLAY_LABELS[index];
        }

        /// <summary>Where the window is on the screen, and how big.</summary>
        internal Rect WindowRect
        {
            get { return windowRect; }
            set { windowRect = value; }
        }

        // Mod+F6 shows or hides the window, the same key for every KSP Diag. Static: the choice holds from one
        // flight scene to the next.
        private static readonly KeyBinding WINDOW_KEY = new KeyBinding(KeyCode.F6);
        private static bool windowVisible = true;

        /// <summary>Whether the window shows, as Mod+F6 toggles it; the measures go on either way.</summary>
        internal static bool WindowVisible
        {
            get { return windowVisible; }
            set { windowVisible = value; }
        }

        private void Update()
        {
            if (GameSettings.MODIFIER_KEY.GetKey() && WINDOW_KEY.GetKeyDown())
            {
                windowVisible = !windowVisible;
            }
            if (Time.unscaledTime < nextRefresh)
            {
                return;
            }
            nextRefresh = Time.unscaledTime + REFRESH_PERIOD;
            Refresh();
        }

        private void LateUpdate()
        {
            if (display == Display.Nothing)
            {
                return;
            }
            foreach (GroundOverlay overlay in ground.Values)
            {
                overlay.Draw(groundLine, DRAW_LAYER);
            }
            foreach (ColliderOverlay overlay in overlays.Values)
            {
                Collider collider = overlay.Collider;
                if (collider != null && !hidden.Contains(collider))
                {
                    overlay.Draw(collider.enabled ? activeLines[overlay.ColorIndex] : disabledLine, DRAW_LAYER);
                }
            }
        }

        /// <summary>
        /// Finds the colliders near the active vessel as they are now, and brings the drawing and the
        /// window in line with them.
        /// </summary>
        private void Refresh()
        {
            body = FlightGlobals.currentMainBody;
            PQS current = body != null ? body.pqsController : null;
            if (current != sphere)
            {
                Clear();
                sphere = current;
                nextScan = 0f;
            }
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (sphere == null || sphere.quads == null || vessel == null)
            {
                return;
            }
            Vector3 center = vessel.transform.position;

            RefreshGround(center);
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + SCAN_PERIOD;
                ScanStatics();
            }
            RefreshColliders(center, vessel);
            CountDrawn();
        }

        /// <summary>
        /// Makes <see cref="ground"/> hold one overlay per terrain quad whose collider comes within
        /// <see cref="RADIUS"/> of <paramref name="center"/>, and nothing else.
        /// </summary>
        private void RefreshGround(Vector3 center)
        {
            nearQuads.Clear();
            foreach (PQ root in sphere.quads)
            {
                CollectQuads(root, center);
            }

            RemoveMissing(ground, nearQuads, overlay => overlay.Destroy());
            foreach (PQ quad in nearQuads)
            {
                if (!ground.TryGetValue(quad, out GroundOverlay overlay))
                {
                    overlay = new GroundOverlay(quad);
                    ground.Add(quad, overlay);
                }
                overlay.Refresh();
            }
        }

        /// <summary>Adds to <see cref="nearQuads"/> the quads under <paramref name="quad"/>,
        /// <paramref name="quad"/> included, whose collider is on and comes within <see cref="RADIUS"/> of
        /// <paramref name="center"/>.</summary>
        private void CollectQuads(PQ quad, Vector3 center)
        {
            if (quad == null)
            {
                return;
            }

            // Stock puts a collider on every quad from a level near the highest one: whatever its level,
            // a quad whose collider is on is ground the physics sees.
            MeshCollider collider = quad.meshCollider;
            if (collider != null && collider.enabled && collider.sharedMesh != null
                && collider.gameObject.activeInHierarchy
                && collider.bounds.SqrDistance(center) <= RADIUS * RADIUS)
            {
                nearQuads.Add(quad);
            }
            if (quad.isSubdivided && quad.subNodes != null)
            {
                foreach (PQ child in quad.subNodes)
                {
                    CollectQuads(child, center);
                }
            }
        }

        /// <summary>Fills <see cref="staticColliders"/> with every solid collider of the statics of
        /// <see cref="sphere"/>, the ones switched off included.</summary>
        private void ScanStatics()
        {
            staticColliders.Clear();

            // FindObjectsOfTypeAll also finds the statics of other bodies and the inactive ones: the
            // checks below keep those of this body in the loaded scene. A static is found through its
            // PQSMod rather than as a child of the sphere, since a mod may move it out of the sphere.
            foreach (PQSCity city in Resources.FindObjectsOfTypeAll<PQSCity>())
            {
                AddColliders(city);
            }
            foreach (PQSCity2 city in Resources.FindObjectsOfTypeAll<PQSCity2>())
            {
                AddColliders(city);
            }
        }

        private void AddColliders(PQSMod city)
        {
            if (city == null || city.sphere != sphere || !city.gameObject.scene.IsValid())
            {
                return;
            }
            // A collider switched off is found too, unlike through any Physics query.
            city.GetComponentsInChildren(true, found);
            foreach (Collider collider in found)
            {
                // A trigger holds nothing up.
                if (!collider.isTrigger)
                {
                    staticColliders.Add(collider);
                }
            }
        }

        /// <summary>
        /// Makes <see cref="overlays"/> hold one overlay per collider that comes within <see cref="RADIUS"/>
        /// of <paramref name="center"/> and is drawn in colour, and nothing else: the colliders of the quads
        /// in <see cref="nearQuads"/>, whatever the layer, the colliders of the statics of the chosen layer,
        /// the ones switched off included, and any other collider of that layer that is on, but for the parts
        /// of <paramref name="vessel"/>.
        /// </summary>
        private void RefreshColliders(Vector3 center, Vessel vessel)
        {
            nearColliders.Clear();
            foreach (Collider collider in staticColliders)
            {
                // An object switched off takes all its children out of the world: the other levels of an
                // upgradeable building, the ruins of an intact one. Only a collider switched off itself
                // in an active object is drawn as switched off.
                if (collider == null || !collider.gameObject.activeInHierarchy || collider.gameObject.layer != layer)
                {
                    continue;
                }
                if (IsNear(collider, center))
                {
                    nearColliders.Add(collider);
                }
            }

            // The ground, whatever the layer, as for the grey outline of its cells: the quads near enough
            // were just collected.
            foreach (PQ quad in nearQuads)
            {
                nearColliders.Add(quad.meshCollider);
            }

            // Whatever else the physics has on that layer, though only what is on: a Physics query does
            // not see a collider switched off. The quads were taken above.
            foreach (Collider collider in Physics.OverlapSphere(center, RADIUS, 1 << layer, QueryTriggerInteraction.Ignore))
            {
                if (nearColliders.Contains(collider) || collider.GetComponent<PQ>() != null)
                {
                    continue;
                }
                Part part = collider.GetComponentInParent<Part>();
                if (part != null && part.vessel == vessel)
                {
                    continue;
                }
                if (IsNear(collider, center))
                {
                    nearColliders.Add(collider);
                }
            }

            RemoveMissing(overlays, nearColliders, overlay => overlay.Destroy());
            foreach (Collider collider in nearColliders)
            {
                if (!overlays.TryGetValue(collider, out ColliderOverlay overlay))
                {
                    overlay = new ColliderOverlay(collider) { ColorIndex = LeastUsedColor() };
                    overlays.Add(collider, overlay);
                }
                overlay.Refresh();
            }

            listed.Clear();
            listed.AddRange(overlays.Values);
            listed.Sort((a, b) => string.CompareOrdinal(ShortName(a.Collider), ShortName(b.Collider)));
        }

        /// <summary>Tells whether <paramref name="collider"/> is of a kind that can be drawn and comes within
        /// <see cref="RADIUS"/> of <paramref name="center"/>.</summary>
        private static bool IsNear(Collider collider, Vector3 center)
        {
            return ColliderOverlay.Reach(collider, out Vector3 at, out float reach)
                && Vector3.Distance(at, center) - reach <= RADIUS;
        }

        /// <summary>The colour of the palette that the fewest colliders drawn now have, the first one on a
        /// tie: each collider has a colour of its own until there are more colliders than colours.</summary>
        private int LeastUsedColor()
        {
            int[] uses = new int[PALETTE.Length];
            foreach (ColliderOverlay overlay in overlays.Values)
            {
                uses[overlay.ColorIndex]++;
            }
            int least = 0;
            for (int i = 1; i < uses.Length; i++)
            {
                if (uses[i] < uses[least])
                {
                    least = i;
                }
            }
            return least;
        }

        /// <summary>The name of the collider's object after that of its parent, which often tells apart
        /// objects of the same name; the name of the quad alone for the ground.</summary>
        private static string ShortName(Collider collider)
        {
            if (collider == null)
            {
                return "";
            }
            // Every quad has the same parent, and its own name already says the body.
            if (QuadOf(collider) != null)
            {
                return collider.name;
            }
            Transform parent = collider.transform.parent;
            return (parent != null ? parent.name + "/" : "") + collider.name;
        }

        /// <summary>Removes from <paramref name="overlays"/>, releasing them, the entries whose key is no
        /// longer in <paramref name="keep"/>.</summary>
        private static void RemoveMissing<TKey, TOverlay>(Dictionary<TKey, TOverlay> overlays, HashSet<TKey> keep,
            System.Action<TOverlay> release) where TKey : Object
        {
            List<TKey> gone = null;
            foreach (KeyValuePair<TKey, TOverlay> entry in overlays)
            {
                if (entry.Key == null || !keep.Contains(entry.Key))
                {
                    (gone ?? (gone = new List<TKey>())).Add(entry.Key);
                }
            }
            if (gone == null)
            {
                return;
            }
            foreach (TKey key in gone)
            {
                release(overlays[key]);
                overlays.Remove(key);
            }
        }

        /// <summary>Drops every overlay.</summary>
        private void Clear()
        {
            foreach (GroundOverlay overlay in ground.Values)
            {
                overlay.Destroy();
            }
            foreach (ColliderOverlay overlay in overlays.Values)
            {
                overlay.Destroy();
            }
            ground.Clear();
            overlays.Clear();
            listed.Clear();
            hidden.Clear();
            staticColliders.Clear();
        }

        /// <summary>Counts the colliders drawn in colour, on and switched off, for the window.</summary>
        private void CountDrawn()
        {
            int active = 0;
            int disabled = 0;
            foreach (Collider collider in overlays.Keys)
            {
                if (collider != null && collider.enabled)
                {
                    active++;
                }
                else
                {
                    disabled++;
                }
            }
            drawnActive = active;
            drawnDisabled = disabled;
        }
    }
}
