using Godot;
using System;

/// <summary>
/// Shared pointy-top regular-hexagon geometry for UI.
///
/// It uses the same equilateral-triangle sqrt(3) metric as TriangleGrid, but
/// stays independent from world/grid domain types.
/// </summary>
public static class HexGridGeometry {
    public static float Sqrt3 =>
        Mathf.Sqrt(3.0f);

    public static Vector2 GetHexSize(
        float radius) {

        ValidateRadius(
            radius);

        return new Vector2(
            Sqrt3 * radius,
            2.0f * radius);
    }

    /// <summary>
    /// Vertical center-to-center distance between neighboring honeycomb rows.
    /// </summary>
    public static float GetRowStep(
        float radius) {

        ValidateRadius(
            radius);

        return 1.5f * radius;
    }

    public static float GetOddRowOffset(
        float radius) {

        return GetHexSize(
            radius).X * 0.5f;
    }

    /// <summary>
    /// Six pointy-top hex vertices in clockwise order, fitted to size.
    /// </summary>
    public static Vector2[] GetVertices(
        Vector2 size) {

        float radius =
            Mathf.Min(
                size.Y * 0.5f,
                size.X / Sqrt3);

        Vector2 center =
            size * 0.5f;

        float halfWidth =
            Sqrt3 * radius * 0.5f;

        float halfRadius =
            radius * 0.5f;

        return new[] {
            center + new Vector2(
                0.0f,
                -radius),

            center + new Vector2(
                halfWidth,
                -halfRadius),

            center + new Vector2(
                halfWidth,
                halfRadius),

            center + new Vector2(
                0.0f,
                radius),

            center + new Vector2(
                -halfWidth,
                halfRadius),

            center + new Vector2(
                -halfWidth,
                -halfRadius),
        };
    }

    /// <summary>
    /// Point-in-hex test for the pointy-top hex fitted to size.
    /// </summary>
    public static bool ContainsPoint(
        Vector2 point,
        Vector2 size) {

        if (size.X <= 0.0f ||
            size.Y <= 0.0f) {
            return false;
        }

        float radius =
            Mathf.Min(
                size.Y * 0.5f,
                size.X / Sqrt3);

        Vector2 center =
            size * 0.5f;

        float x =
            Mathf.Abs(
                point.X - center.X);

        float y =
            Mathf.Abs(
                point.Y - center.Y);

        float halfWidth =
            Sqrt3 * radius * 0.5f;

        if (x > halfWidth ||
            y > radius) {
            return false;
        }

        return x +
            Sqrt3 * y
            <= Sqrt3 * radius;
    }

    private static void ValidateRadius(
        float radius) {

        if (radius <= 0.0f) {
            throw new ArgumentOutOfRangeException(
                nameof(radius),
                "Hex radius must be greater than zero.");
        }
    }
}
