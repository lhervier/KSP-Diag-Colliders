using System;
using System.Collections.Generic;
using UnityEngine;
using com.github.lhervier.ksp.mcpserver;

namespace com.github.lhervier.ksp.diag.colliders
{
    /// <summary>
    /// What KSP-MCPServer, when it is installed, offers of this mod as tools: what its window shows, its
    /// buttons and modes, and the moving of the window. Each tool returns no more than the window shows or
    /// the log receives. Each method works on the mod loaded in the flight scene. Nothing here is needed to
    /// play by hand, and this mod runs the same without KSP-MCPServer: only that server reads the attribute.
    /// </summary>
    internal static class McpTools
    {
        [McpTool("colliders_read",
            "Reads what the window of KSP Diag - Colliders shows, without writing anything: the body (empty when " +
                "there is none), the radius in metres, the display mode, the layer and its name (layerName), how many " +
                "ground quads (groundQuads) and colliders of statics on (collidersOn) and switched off (collidersOff) " +
                "are drawn, and the list of the colliders (colliders), each with its name as listed (parent/name), " +
                "its colour, whether it is on, convex (the physics uses its hull) or unreadable (the box of its " +
                "bounds is drawn), and whether it is ticked.")]
        internal static object Read()
        {
            return Mod().Read();
        }

        [McpTool("colliders_log_under_crafts",
            "Presses Log the colliders under each craft in the window of KSP Diag - Colliders: for every loaded " +
                "craft, its name (craft), situation, latitude and longitude, and every collider of the ground and " +
                "the statics a ray fired straight down under it meets, nearest first, with its parent, its height " +
                "above the terrain the game computes there (heightMm) and whether it is active. Written to KSP.log " +
                "as well; the numbers are rounded as the log writes them.")]
        internal static object LogUnderCrafts()
        {
            return Mod().LogUnderCrafts();
        }

        [McpTool("colliders_move_window",
            "Moves the window of KSP Diag - Colliders, as dragging it does: x and y in pixels from the top left " +
            "corner of the screen. Returns its position and size (x, y, width, height).")]
        internal static object MoveWindow(double x, double y)
        {
            KSPDiagColliders mod = Mod();
            Rect rect = mod.WindowRect;
            rect.x = (float)x;
            rect.y = (float)y;
            mod.WindowRect = rect;
            return new Dictionary<string, object>
            {
                { "x", (double)rect.x },
                { "y", (double)rect.y },
                { "width", (double)rect.width },
                { "height", (double)rect.height }
            };
        }

        [McpTool("colliders_show_window",
            "Shows or hides the window of KSP Diag - Colliders, as Mod+F6 does; what it measures goes on either " +
            "way. Returns whether it shows (visible).")]
        internal static object ShowWindow(bool visible)
        {
            KSPDiagColliders.WindowVisible = visible;
            return new Dictionary<string, object> { { "visible", KSPDiagColliders.WindowVisible } };
        }

        [McpTool("colliders_set_display",
            "Chooses how KSP Diag - Colliders draws the colliders, as clicking one of the three modes of its window " +
            "does: 0 Hidden by what stands in front (the default), 1 Through everything, 2 Off. What it measures " +
            "goes on in every mode. Returns the mode now chosen (display).")]
        internal static object SetDisplay(double mode)
        {
            return new Dictionary<string, object> { { "display", Mod().ChooseDisplay((int)mode) } };
        }

        private static KSPDiagColliders Mod()
        {
            KSPDiagColliders mod = UnityEngine.Object.FindObjectOfType<KSPDiagColliders>();
            if (mod == null)
            {
                throw new InvalidOperationException("KSP Diag - Colliders only runs in flight");
            }
            return mod;
        }
    }
}
