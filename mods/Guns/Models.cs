using UnityEngine;
using static Guns.Model;

namespace Guns
{
    /// <summary>
    /// The guns' models, from plain shapes. Barrel along +Z, up is +Y. Each is drawn around its gun, then moved so the place the hand holds
    /// (the shift: minus the grip's centre) is the origin, where the game puts the hand. Marked parts: "dh_muzzle" (where rounds and the
    /// flash come out), "dh_eject" (brass), "dh_action" (the part that moves when it fires: a slide, bolt, pump or lever, see GunDef.Action*),
    /// "dh_spin" (barrels that turn).
    /// </summary>
    internal static class Models
    {
        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        // ---- a pistol, for the Pistol and the pair ----------------------------------------------------------------

        private static void PistolParts(Builder b, string tag, Color slideColor, Color frameColor)
        {
            b.Cube("frame", V(0f, -0.002f, 0.0f), V(0.030f, 0.030f, 0.21f), frameColor);
            Builder slide = b.Group("dh_action" + tag, V(0f, 0f, 0f));
            slide.Cube("slide", V(0f, 0.032f, 0.01f), V(0.031f, 0.036f, 0.27f), slideColor, metal: 0.6f);
            slide.Cube("port", V(0.0158f, 0.034f, 0.0f), V(0.002f, 0.016f, 0.05f), Black);
            slide.Cube("rearsight", V(0f, 0.056f, -0.115f), V(0.020f, 0.010f, 0.012f), Black);
            slide.Cube("frontsight", V(0f, 0.056f, 0.13f), V(0.008f, 0.012f, 0.010f), Black);
            slide.Cube("serrations", V(0f, 0.034f, -0.10f), V(0.033f, 0.026f, 0.03f), Black);
            b.Tube("barrel", V(0f, 0.030f, 0.15f), 0.016f, 0.05f, Black);
            b.Cube("grip", V(0f, -0.075f, -0.085f), V(0.033f, 0.125f, 0.056f), frameColor, V(15f, 0f, 0f));
            b.Cube("butt", V(0f, -0.140f, -0.103f), V(0.036f, 0.014f, 0.062f), Black, V(15f, 0f, 0f));
            b.Cube("guardbottom", V(0f, -0.058f, 0.02f), V(0.010f, 0.008f, 0.075f), frameColor);
            b.Cube("guardfront", V(0f, -0.040f, 0.056f), V(0.010f, 0.040f, 0.010f), frameColor);
            b.Cube("trigger", V(0f, -0.030f, 0.014f), V(0.008f, 0.026f, 0.008f), Black);
            b.Point("dh_muzzle" + tag, V(0f, 0.030f, 0.18f));
            b.Point("dh_eject" + tag, V(0.02f, 0.042f, -0.01f));
        }

        internal static Bounds Pistol(Transform root, Material mat)
        {
            var b = new Builder(root, mat, V(0f, 0.075f, 0.085f));
            PistolParts(b, "", Steel, Poly);
            return b.Bounds;
        }

        /// <summary>Two pistols. In the model they are stacked (the picture and a dropped one show both); in the hands Aim.Pose puts the second in the left.</summary>
        internal static Bounds DualPistols(Transform root, Material mat)
        {
            var b = new Builder(root, mat, Vector3.zero);
            PistolParts(b.Group("pistolR", V(0f, 0.075f, 0.085f)), "", Nickel, Black);
            PistolParts(b.Group("pistolL", V(0f, 0.075f + 0.15f, 0.085f)), "2", Nickel, Black);
            return b.Bounds;
        }

        // ---- a revolver -----------------------------------------------------------------------------------------------

        internal static Bounds Revolver(Transform root, Material mat)
        {
            var b = new Builder(root, mat, V(0f, 0.075f, 0.095f));
            b.Cube("frame", V(0f, 0.0f, -0.02f), V(0.032f, 0.07f, 0.13f), Nickel, metal: 0.7f);
            b.Tube("cylinder", V(0f, 0.012f, 0.075f), 0.064f, 0.08f, Nickel, metal: 0.7f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                b.Tube("chamber" + i, V(Mathf.Sin(a) * 0.022f, 0.012f + Mathf.Cos(a) * 0.022f, 0.1155f), 0.014f, 0.004f, Black);
            }
            b.Tube("barrel", V(0f, 0.030f, 0.24f), 0.026f, 0.26f, Blued, metal: 0.7f);
            b.Cube("lug", V(0f, 0.008f, 0.19f), V(0.022f, 0.03f, 0.11f), Blued, metal: 0.7f);
            b.Tube("rod", V(0f, -0.004f, 0.215f), 0.010f, 0.115f, Nickel, metal: 0.7f);
            b.Cube("topstrap", V(0f, 0.047f, -0.005f), V(0.022f, 0.012f, 0.15f), Nickel, metal: 0.7f);
            b.Cube("rearsight", V(0f, 0.056f, -0.065f), V(0.016f, 0.010f, 0.012f), Black);
            b.Cube("frontsight", V(0f, 0.050f, 0.36f), V(0.006f, 0.016f, 0.010f), Black);
            b.Cube("hammer", V(0f, 0.046f, -0.085f), V(0.010f, 0.030f, 0.020f), Black, V(-30f, 0f, 0f));
            b.Cube("grip", V(0f, -0.075f, -0.10f), V(0.036f, 0.125f, 0.062f), DarkWood, V(14f, 0f, 0f));
            b.Cube("cap", V(0f, -0.140f, -0.117f), V(0.040f, 0.010f, 0.068f), Nickel, V(14f, 0f, 0f));
            b.Cube("guardbottom", V(0f, -0.050f, 0.01f), V(0.008f, 0.008f, 0.08f), Nickel);
            b.Cube("guardfront", V(0f, -0.032f, 0.052f), V(0.008f, 0.040f, 0.008f), Nickel);
            b.Cube("trigger", V(0f, -0.030f, 0.0f), V(0.007f, 0.024f, 0.007f), Black);
            b.Point("dh_muzzle", V(0f, 0.030f, 0.375f));
            b.Point("dh_eject", V(0.02f, 0.02f, 0.0f));
            return b.Bounds;
        }

        // ---- a submachine gun ---------------------------------------------------------------------------------------

        internal static Bounds Smg(Transform root, Material mat)
        {
            var b = new Builder(root, mat, V(0f, 0.07f, 0.07f));
            b.Cube("receiver", V(0f, 0.02f, 0.0f), V(0.046f, 0.062f, 0.30f), Poly);
            b.Cube("cover", V(0f, 0.056f, 0.0f), V(0.038f, 0.012f, 0.28f), Steel);
            b.Tube("shroud", V(0f, 0.026f, 0.22f), 0.042f, 0.15f, Steel);
            b.Tube("barrel", V(0f, 0.026f, 0.31f), 0.017f, 0.04f, Black);
            b.Cube("grip", V(0f, -0.07f, -0.07f), V(0.034f, 0.125f, 0.048f), Poly, V(12f, 0f, 0f));
            b.Cube("magazine", V(0f, -0.125f, 0.07f), V(0.028f, 0.21f, 0.046f), Steel, V(-8f, 0f, 0f));
            b.Cube("foregrip", V(0f, -0.06f, 0.17f), V(0.030f, 0.075f, 0.036f), Poly);
            b.Cube("guard", V(0f, -0.032f, -0.015f), V(0.008f, 0.008f, 0.09f), Poly);
            b.Cube("trigger", V(0f, -0.016f, -0.012f), V(0.007f, 0.024f, 0.007f), Black);
            b.Tube("stock", V(0f, 0.02f, -0.27f), 0.012f, 0.24f, Steel);
            b.Cube("buttplate", V(0f, 0.012f, -0.395f), V(0.030f, 0.075f, 0.016f), Poly);
            b.Cube("frontsight", V(0f, 0.078f, 0.18f), V(0.006f, 0.026f, 0.006f), Black);
            b.Cube("rearsight", V(0f, 0.072f, -0.10f), V(0.020f, 0.020f, 0.010f), Black);
            Builder handle = b.Group("dh_action", V(0f, 0.06f, -0.05f));
            handle.Cube("charging", V(0f, 0.004f, 0f), V(0.014f, 0.014f, 0.04f), Brass);
            b.Point("dh_muzzle", V(0f, 0.026f, 0.335f));
            b.Point("dh_eject", V(0.03f, 0.04f, 0.0f));
            return b.Bounds;
        }

        // ---- a pump shotgun -------------------------------------------------------------------------------------------

        internal static Bounds Shotgun(Transform root, Material mat)
        {
            var b = new Builder(root, mat, V(0f, 0.035f, 0.12f));
            b.Cube("receiver", V(0f, 0.0f, 0.05f), V(0.042f, 0.068f, 0.32f), Steel, metal: 0.6f);
            b.Tube("barrel", V(0f, 0.022f, 0.43f), 0.028f, 0.58f, Blued, metal: 0.6f);
            b.Tube("tube", V(0f, -0.014f, 0.40f), 0.030f, 0.50f, Blued, metal: 0.6f);
            b.Cube("rib", V(0f, 0.039f, 0.43f), V(0.010f, 0.006f, 0.58f), Black);
            b.Tube("cap", V(0f, -0.014f, 0.655f), 0.036f, 0.03f, Black);
            b.Ball("bead", V(0f, 0.044f, 0.715f), V(0.010f, 0.010f, 0.010f), Brass);
            Builder pump = b.Group("dh_action", V(0f, -0.014f, 0.31f));
            pump.Cube("forend", V(0f, 0f, 0f), V(0.050f, 0.054f, 0.20f), Wood, metal: 0.1f, gloss: 0.5f);
            pump.Cube("grooves", V(0f, -0.014f, 0f), V(0.052f, 0.012f, 0.12f), DarkWood, metal: 0.1f);
            b.Cube("stock", V(0f, -0.03f, -0.28f), V(0.050f, 0.105f, 0.36f), Wood, V(-4f, 0f, 0f), 0.1f, 0.5f);
            b.Cube("wrist", V(0f, -0.045f, -0.09f), V(0.040f, 0.07f, 0.09f), Wood, V(10f, 0f, 0f), 0.1f, 0.5f);
            b.Cube("pad", V(0f, -0.055f, -0.465f), V(0.056f, 0.135f, 0.024f), Black, V(-4f, 0f, 0f));
            b.Cube("guard", V(0f, -0.05f, 0.0f), V(0.010f, 0.008f, 0.09f), Steel);
            b.Cube("trigger", V(0f, -0.034f, -0.005f), V(0.007f, 0.024f, 0.007f), Black);
            b.Point("dh_muzzle", V(0f, 0.022f, 0.735f));
            b.Point("dh_eject", V(0.03f, 0.016f, 0.02f));
            return b.Bounds;
        }

        // ---- a bolt-action hunting rifle ------------------------------------------------------------------------------

        internal static Bounds Rifle(Transform root, Material mat)
        {
            var b = new Builder(root, mat, V(0f, 0.06f, 0.06f));
            b.Cube("fore", V(0f, -0.04f, 0.33f), V(0.040f, 0.058f, 0.40f), Wood, metal: 0.1f, gloss: 0.5f);
            b.Cube("stock", V(0f, -0.05f, -0.22f), V(0.046f, 0.10f, 0.36f), Wood, V(-4f, 0f, 0f), 0.1f, 0.5f);
            b.Cube("grip", V(0f, -0.065f, -0.06f), V(0.038f, 0.115f, 0.056f), Wood, V(14f, 0f, 0f), 0.1f, 0.5f);
            b.Cube("pad", V(0f, -0.055f, -0.41f), V(0.050f, 0.125f, 0.02f), Black, V(-4f, 0f, 0f));
            b.Tube("receiver", V(0f, 0.012f, 0.06f), 0.040f, 0.34f, Steel, metal: 0.6f);
            b.Tube("barrel", V(0f, 0.012f, 0.52f), 0.022f, 0.62f, Blued, metal: 0.6f);
            b.Cube("magbox", V(0f, -0.032f, 0.02f), V(0.030f, 0.04f, 0.10f), Steel);
            b.Cube("guard", V(0f, -0.07f, 0.02f), V(0.008f, 0.008f, 0.11f), Steel);
            b.Cube("trigger", V(0f, -0.05f, 0.0f), V(0.007f, 0.024f, 0.007f), Black);
            b.Cube("frontsight", V(0f, 0.034f, 0.81f), V(0.006f, 0.014f, 0.010f), Black);
            b.Cube("rearsight", V(0f, 0.034f, 0.17f), V(0.020f, 0.010f, 0.014f), Black);
            Builder bolt = b.Group("dh_action", V(0f, 0.015f, -0.04f));
            bolt.Tube("stem", V(0.03f, 0f, 0f), 0.009f, 0.06f, Steel, 'x');
            bolt.Ball("knob", V(0.065f, 0f, 0f), V(0.022f, 0.022f, 0.022f), Black);
            bolt.Tube("body", V(0f, 0f, 0.01f), 0.016f, 0.06f, Steel);
            b.Point("dh_muzzle", V(0f, 0.012f, 0.84f));
            b.Point("dh_eject", V(0.025f, 0.03f, 0.03f));
            return b.Bounds;
        }

        // ---- a lever-action rifle -------------------------------------------------------------------------------------

        internal static Bounds LeverAction(Transform root, Material mat)
        {
            var b = new Builder(root, mat, V(0f, 0.045f, 0.10f));
            b.Cube("receiver", V(0f, 0.0f, 0.05f), V(0.040f, 0.072f, 0.32f), Brass, metal: 0.8f, gloss: 0.6f);
            b.Tube("barrel", V(0f, 0.026f, 0.41f), 0.027f, 0.52f, Blued, metal: 0.6f);
            b.Tube("tube", V(0f, -0.012f, 0.40f), 0.022f, 0.48f, Blued, metal: 0.6f);
            b.Tube("cap", V(0f, -0.012f, 0.645f), 0.028f, 0.02f, Brass, metal: 0.8f);
            b.Cube("fore", V(0f, -0.034f, 0.27f), V(0.038f, 0.044f, 0.30f), Wood, metal: 0.1f, gloss: 0.5f);
            b.Cube("stock", V(0f, -0.04f, -0.24f), V(0.046f, 0.10f, 0.35f), Wood, V(-4f, 0f, 0f), 0.1f, 0.5f);
            b.Cube("pad", V(0f, -0.05f, -0.42f), V(0.050f, 0.12f, 0.02f), Brass, V(-4f, 0f, 0f), 0.8f, 0.6f);
            b.Cube("hammer", V(0f, 0.050f, -0.105f), V(0.010f, 0.030f, 0.02f), Black, V(-35f, 0f, 0f));
            b.Cube("frontsight", V(0f, 0.046f, 0.66f), V(0.006f, 0.018f, 0.010f), Black);
            b.Cube("rearsight", V(0f, 0.046f, 0.21f), V(0.020f, 0.010f, 0.016f), Black);
            // the lever is a loop under the receiver that swings down and forward about its rear end
            Builder lever = b.Group("dh_action", V(0f, -0.04f, -0.075f));
            lever.Cube("bar", V(0f, -0.055f, 0.075f), V(0.008f, 0.008f, 0.15f), Blued, metal: 0.6f);
            lever.Cube("front", V(0f, -0.025f, 0.15f), V(0.008f, 0.06f, 0.008f), Blued, metal: 0.6f);
            lever.Cube("rear", V(0f, -0.025f, 0.0f), V(0.008f, 0.06f, 0.008f), Blued, metal: 0.6f);
            b.Cube("trigger", V(0f, -0.045f, -0.005f), V(0.007f, 0.024f, 0.007f), Black);
            b.Point("dh_muzzle", V(0f, 0.026f, 0.68f));
            b.Point("dh_eject", V(0.025f, 0.03f, 0.0f));
            return b.Bounds;
        }

        // ---- an AR-15 -------------------------------------------------------------------------------------------------

        internal static Bounds Ar15(Transform root, Material mat)
        {
            var b = new Builder(root, mat, V(0f, 0.07f, 0.10f));
            b.Cube("lower", V(0f, -0.012f, -0.035f), V(0.036f, 0.07f, 0.20f), Black);
            b.Cube("upper", V(0f, 0.046f, 0.02f), V(0.038f, 0.056f, 0.28f), Black);
            b.Cube("rail", V(0f, 0.080f, 0.04f), V(0.020f, 0.010f, 0.34f), Black);
            b.Tube("handguard", V(0f, 0.03f, 0.31f), 0.052f, 0.34f, Black);
            b.Tube("barrel", V(0f, 0.03f, 0.56f), 0.016f, 0.20f, Steel, metal: 0.6f);
            b.Cube("gasblock", V(0f, 0.052f, 0.465f), V(0.026f, 0.030f, 0.04f), Steel);
            b.Cube("frontpost", V(0f, 0.085f, 0.465f), V(0.007f, 0.042f, 0.007f), Black);
            b.Tube("flashhider", V(0f, 0.03f, 0.675f), 0.028f, 0.065f, Black);
            b.Cube("rearsight", V(0f, 0.095f, -0.10f), V(0.012f, 0.026f, 0.010f), Black);
            b.Cube("sightbody", V(0f, 0.116f, 0.04f), V(0.034f, 0.046f, 0.08f), Black, metal: 0.2f);
            b.Cube("sightwindow", V(0f, 0.120f, 0.04f), V(0.028f, 0.026f, 0.082f), Lens, metal: 0.9f, gloss: 0.9f);
            b.Cube("grip", V(0f, -0.085f, -0.10f), V(0.034f, 0.13f, 0.05f), Black, V(20f, 0f, 0f));
            b.Cube("magazine", V(0f, -0.10f, 0.05f), V(0.030f, 0.17f, 0.060f), Steel, V(-10f, 0f, 0f));
            b.Cube("guard", V(0f, -0.054f, -0.015f), V(0.008f, 0.006f, 0.10f), Black);
            b.Cube("trigger", V(0f, -0.036f, -0.02f), V(0.007f, 0.022f, 0.007f), Black);
            b.Tube("buffer", V(0f, 0.01f, -0.29f), 0.032f, 0.20f, Black);
            b.Cube("stock", V(0f, -0.012f, -0.36f), V(0.040f, 0.095f, 0.17f), Black, V(-5f, 0f, 0f));
            b.Cube("pad", V(0f, -0.015f, -0.447f), V(0.042f, 0.105f, 0.014f), Black, V(-5f, 0f, 0f));
            Builder carrier = b.Group("dh_action", V(0.0205f, 0.046f, 0.0f));
            carrier.Cube("carrier", V(0f, 0f, 0f), V(0.002f, 0.022f, 0.05f), Steel, metal: 0.7f);
            b.Point("dh_muzzle", V(0f, 0.03f, 0.715f));
            b.Point("dh_eject", V(0.03f, 0.046f, 0.0f));
            return b.Bounds;
        }

        // ---- a sniper rifle -------------------------------------------------------------------------------------------

        internal static Bounds Sniper(Transform root, Material mat)
        {
            var b = new Builder(root, mat, V(0f, 0.085f, 0.09f));
            b.Cube("fore", V(0f, -0.035f, 0.33f), V(0.050f, 0.062f, 0.46f), Olive, metal: 0.1f, gloss: 0.3f);
            b.Cube("stock", V(0f, -0.04f, -0.27f), V(0.052f, 0.115f, 0.38f), Olive, V(-3f, 0f, 0f), 0.1f, 0.3f);
            b.Cube("cheek", V(0f, 0.032f, -0.30f), V(0.042f, 0.04f, 0.22f), Olive, metal: 0.1f, gloss: 0.3f);
            b.Cube("grip", V(0f, -0.085f, -0.09f), V(0.040f, 0.13f, 0.056f), Olive, V(18f, 0f, 0f), 0.1f, 0.3f);
            b.Cube("pad", V(0f, -0.045f, -0.47f), V(0.056f, 0.14f, 0.024f), Black, V(-3f, 0f, 0f));
            b.Tube("receiver", V(0f, 0.015f, 0.04f), 0.046f, 0.40f, Steel, metal: 0.6f);
            b.Tube("barrel", V(0f, 0.015f, 0.62f), 0.030f, 0.76f, Blued, metal: 0.7f);
            b.Tube("brake", V(0f, 0.015f, 1.04f), 0.042f, 0.09f, Black);
            b.Cube("magbox", V(0f, -0.045f, 0.02f), V(0.036f, 0.07f, 0.11f), Steel);
            b.Cube("guard", V(0f, -0.07f, 0.0f), V(0.008f, 0.008f, 0.11f), Steel);
            b.Cube("trigger", V(0f, -0.05f, -0.02f), V(0.007f, 0.024f, 0.007f), Black);
            // the scope: long, with a bell at each end, on two rings
            b.Tube("scope", V(0f, 0.095f, 0.02f), 0.044f, 0.36f, Black, metal: 0.5f, gloss: 0.6f);
            b.Tube("bellfront", V(0f, 0.095f, 0.21f), 0.066f, 0.09f, Black, metal: 0.5f, gloss: 0.6f);
            b.Tube("bellrear", V(0f, 0.095f, -0.19f), 0.056f, 0.07f, Black, metal: 0.5f, gloss: 0.6f);
            b.Tube("lensfront", V(0f, 0.095f, 0.2565f), 0.060f, 0.004f, Lens, metal: 0.9f, gloss: 0.95f);
            b.Tube("lensrear", V(0f, 0.095f, -0.2265f), 0.050f, 0.004f, Lens, metal: 0.9f, gloss: 0.95f);
            b.Tube("turret", V(0f, 0.13f, 0.03f), 0.028f, 0.028f, Black, 'y');
            b.Cube("ringfront", V(0f, 0.058f, 0.10f), V(0.030f, 0.05f, 0.03f), Steel);
            b.Cube("ringrear", V(0f, 0.058f, -0.07f), V(0.030f, 0.05f, 0.03f), Steel);
            b.Tube("bipod1", V(0.026f, -0.082f, 0.50f), 0.010f, 0.20f, Steel);
            b.Tube("bipod2", V(-0.026f, -0.082f, 0.50f), 0.010f, 0.20f, Steel);
            Builder bolt = b.Group("dh_action", V(0f, 0.015f, -0.06f));
            bolt.Tube("stem", V(0.034f, 0f, 0f), 0.011f, 0.07f, Steel, 'x');
            bolt.Ball("knob", V(0.074f, 0f, 0f), V(0.030f, 0.030f, 0.030f), Black);
            bolt.Tube("body", V(0f, 0f, 0.01f), 0.020f, 0.07f, Steel);
            b.Point("dh_muzzle", V(0f, 0.015f, 1.09f));
            b.Point("dh_eject", V(0.03f, 0.04f, 0.0f));
            return b.Bounds;
        }

        // ---- the machine gun ------------------------------------------------------------------------------------------

        internal static Bounds MachineGun(Transform root, Material mat)
        {
            var b = new Builder(root, mat, V(0f, 0.12f, 0.10f));
            b.Cube("receiver", V(0f, 0f, 0f), V(0.075f, 0.12f, 0.42f), Steel);
            b.Cube("rail", V(0f, 0.075f, 0.03f), V(0.035f, 0.03f, 0.34f), Black);
            b.Tube("shroud", V(0f, 0.01f, 0.38f), 0.075f, 0.36f, Steel);
            b.Tube("barrel", V(0f, 0.01f, 0.62f), 0.032f, 0.44f, Black);
            b.Tube("muzzle", V(0f, 0.01f, 0.86f), 0.05f, 0.08f, Black);
            b.Cube("stock", V(0f, -0.02f, -0.36f), V(0.06f, 0.11f, 0.30f), Wood, V(-6f, 0f, 0f), 0.1f, 0.5f);
            b.Cube("buttplate", V(0f, -0.035f, -0.52f), V(0.065f, 0.13f, 0.02f), Black, V(-6f, 0f, 0f));
            b.Cube("grip", V(0f, -0.13f, -0.10f), V(0.05f, 0.15f, 0.06f), Wood, V(18f, 0f, 0f), 0.1f, 0.5f);
            b.Cube("foregrip", V(0f, -0.085f, 0.32f), V(0.05f, 0.12f, 0.055f), Wood, metal: 0.1f, gloss: 0.5f);
            b.Cube("magazine", V(0f, -0.17f, 0.10f), V(0.055f, 0.22f, 0.10f), Steel, V(-8f, 0f, 0f));
            b.Cube("sight", V(0f, 0.105f, 0.17f), V(0.015f, 0.04f, 0.015f), Black);
            Builder bolt = b.Group("dh_action", V(0.042f, 0.03f, 0.02f));
            bolt.Cube("bolt", V(0f, 0f, 0f), V(0.012f, 0.02f, 0.06f), Brass);
            b.Point("dh_muzzle", V(0f, 0.01f, 0.91f));
            b.Point("dh_eject", V(0.045f, 0.04f, 0.06f));
            return b.Bounds;
        }

        // ---- the rotary machine gun -----------------------------------------------------------------------------------

        internal static Bounds Minigun(Transform root, Material mat)
        {
            var b = new Builder(root, mat, V(0f, 0.12f, 0.12f));
            b.Tube("housing", V(0f, 0f, -0.02f), 0.15f, 0.30f, Steel, metal: 0.6f);
            b.Tube("motor", V(0f, 0f, -0.20f), 0.115f, 0.10f, Black);
            b.Tube("collar", V(0f, 0f, 0.15f), 0.17f, 0.04f, Black);
            b.Cube("handle", V(0f, 0.115f, -0.02f), V(0.025f, 0.020f, 0.22f), Black);
            b.Cube("posts1", V(0f, 0.095f, -0.11f), V(0.020f, 0.04f, 0.02f), Black);
            b.Cube("posts2", V(0f, 0.095f, 0.07f), V(0.020f, 0.04f, 0.02f), Black);
            b.Cube("grip", V(0f, -0.12f, -0.12f), V(0.042f, 0.15f, 0.05f), Poly, V(15f, 0f, 0f));
            b.Cube("foregrip", V(0f, -0.115f, 0.12f), V(0.036f, 0.13f, 0.046f), Poly);
            b.Cube("guard", V(0f, -0.075f, -0.07f), V(0.008f, 0.008f, 0.09f), Poly);
            b.Cube("trigger", V(0f, -0.058f, -0.07f), V(0.007f, 0.024f, 0.007f), Black);
            b.Cube("feed", V(-0.10f, -0.06f, 0.0f), V(0.11f, 0.10f, 0.15f), Olive, metal: 0.3f);
            b.Cube("belt", V(-0.045f, -0.005f, 0.0f), V(0.04f, 0.02f, 0.10f), Brass, metal: 0.8f, gloss: 0.6f);
            Builder spin = b.Group("dh_spin", V(0f, 0f, 0f));
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                spin.Tube("barrel" + i, V(Mathf.Sin(a) * 0.045f, Mathf.Cos(a) * 0.045f, 0.46f), 0.024f, 0.64f, Blued, metal: 0.7f);
            }
            spin.Tube("axle", V(0f, 0f, 0.46f), 0.03f, 0.64f, Steel);
            spin.Tube("clamp1", V(0f, 0f, 0.30f), 0.14f, 0.025f, Steel, metal: 0.7f);
            spin.Tube("clamp2", V(0f, 0f, 0.55f), 0.13f, 0.02f, Steel, metal: 0.7f);
            spin.Tube("clamp3", V(0f, 0f, 0.77f), 0.12f, 0.02f, Black);
            b.Point("dh_muzzle", V(0f, 0f, 0.80f));
            b.Point("dh_eject", V(0.075f, 0.0f, 0.04f));
            return b.Bounds;
        }

        // ---- the flamethrower -----------------------------------------------------------------------------------------

        /// <summary>A red fuel tank slung under a long wand with a ribbed heat shield, a valve block with its wheel, pistol and front grips, and a pilot flame always lit under the nozzle.</summary>
        internal static Bounds Flamethrower(Transform root, Material mat)
        {
            var b = new Builder(root, mat, V(0f, 0.12f, 0.14f));
            b.Cube("body", V(0f, 0f, -0.06f), V(0.06f, 0.07f, 0.26f), Steel, metal: 0.5f);
            b.Tube("valve", V(0f, 0.055f, -0.02f), 0.035f, 0.05f, Brass, 'y', metal: 0.8f, gloss: 0.6f);
            b.Tube("wheel", V(0f, 0.085f, -0.02f), 0.07f, 0.012f, Red, 'y');
            b.Tube("tank", V(0f, -0.115f, 0.16f), 0.13f, 0.36f, Red, metal: 0.3f, gloss: 0.5f);
            b.Tube("tankcapF", V(0f, -0.115f, 0.345f), 0.12f, 0.02f, Black);
            b.Tube("tankcapR", V(0f, -0.115f, -0.025f), 0.12f, 0.02f, Black);
            b.Tube("band1", V(0f, -0.115f, 0.06f), 0.136f, 0.015f, Black);
            b.Tube("band2", V(0f, -0.115f, 0.26f), 0.136f, 0.015f, Black);
            b.Tube("filler", V(0f, -0.045f, 0.30f), 0.03f, 0.03f, Brass, 'y', metal: 0.8f, gloss: 0.6f);
            b.Tube("gauge", V(0.068f, -0.10f, 0.10f), 0.045f, 0.015f, Nickel, 'x', metal: 0.7f);
            b.Tube("gaugeface", V(0.076f, -0.10f, 0.10f), 0.036f, 0.004f, new Color(0.85f, 0.83f, 0.76f), 'x', 0f, 0.8f);
            b.Cube("hose", V(0f, -0.035f, 0.39f), V(0.022f, 0.022f, 0.10f), Black, V(-49f, 0f, 0f), 0.1f, 0.3f);
            b.Tube("wand", V(0f, 0f, 0.40f), 0.034f, 0.70f, Steel, metal: 0.6f);
            b.Tube("shield", V(0f, 0f, 0.55f), 0.07f, 0.30f, Black, metal: 0.3f);
            for (int i = 0; i < 4; i++) b.Tube("rib" + i, V(0f, 0f, 0.44f + i * 0.072f), 0.076f, 0.012f, Steel, metal: 0.6f);
            b.Tube("nozzle", V(0f, 0f, 0.77f), 0.05f, 0.05f, Brass, metal: 0.8f, gloss: 0.6f);
            b.Tube("tip", V(0f, 0f, 0.80f), 0.036f, 0.02f, Black);
            b.Tube("igniter", V(0f, -0.038f, 0.745f), 0.014f, 0.07f, Nickel, metal: 0.7f);
            GameObject pilot = b.Ball("pilot", V(0f, -0.038f, 0.79f), V(0.02f, 0.022f, 0.032f), PilotFlame);
            Paint(pilot, mat, PilotFlame, emissive: true);   // (always lit)
            Fx.Own(pilot.GetComponent<Renderer>().sharedMaterial);
            b.Cube("grip", V(0f, -0.12f, -0.14f), V(0.04f, 0.14f, 0.055f), Wood, V(15f, 0f, 0f), 0.1f, 0.5f);
            b.Cube("guard", V(0f, -0.06f, -0.085f), V(0.008f, 0.008f, 0.08f), Black);
            b.Cube("trigger", V(0f, -0.045f, -0.095f), V(0.007f, 0.024f, 0.007f), Black);
            b.Cube("foregrip", V(0f, -0.075f, 0.48f), V(0.035f, 0.12f, 0.045f), Wood, metal: 0.1f, gloss: 0.5f);
            b.Point("dh_muzzle", V(0f, 0f, 0.81f));
            return b.Bounds;
        }

        private static readonly Color PilotFlame = new Color(1f, 0.55f, 0.15f);
    }
}
