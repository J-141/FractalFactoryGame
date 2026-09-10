using System;
using System.Collections.Generic;


public static class TriangleGridRegions {
    /// <summary>
    /// 生成以 lattice vertex (0,0) 为 anchor 的大三角形。
    ///
    /// 三个大顶点为：
    ///
    /// (0, 0)
    /// (sideLength, 0)
    /// (0, sideLength)
    ///
    /// 总 cell 数为 sideLength²。
    /// </summary>
    public static IEnumerable<TriangleGridPosition> Triangle(
        int sideLength) {
        if (sideLength <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(sideLength));

        for (int y = 0; y < sideLength; y++) {
            int downCount =
                sideLength - y;

            int upCount =
                sideLength - y - 1;

            for (int x = 0; x < downCount; x++) {
                yield return new TriangleGridPosition(
                    x,
                    y,
                    TriangleFacing.Down);
            }

            for (int x = 0; x < upCount; x++) {
                yield return new TriangleGridPosition(
                    x,
                    y,
                    TriangleFacing.Up);
            }
        }
    }


    /// <summary>
    /// 生成以 lattice vertex (0,0) 为中心的正六边形。
    ///
    /// sideLength 为六边形边长。
    ///
    /// 六边形由围绕 (0,0) 的六个大三角形组成。
    ///
    /// 总 cell 数为 6 * sideLength²。
    /// </summary>
    public static IEnumerable<TriangleGridPosition> Hexagon(
        int sideLength) {
        if (sideLength <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(sideLength));

        for (int rotation = 0; rotation < 6; rotation++) {
            foreach (
                TriangleGridPosition cell
                in Triangle(sideLength)) {
                yield return TriangleGrid.RotateCell(
                    cell,
                    rotation);
            }
        }
    }
}