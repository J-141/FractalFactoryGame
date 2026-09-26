using System;
using System.Collections.Generic;
using System.Numerics;

public sealed class ShapeStore {
    private readonly ComplexShapePool _complexPool;

    /*
     * 只需要给 Complex Shape 缓存旋转关系。
     *
     * 新 invariant：
     *
     * 对同一个 rotation orbit 中的三个 Shape：
     *
     *   R0 -> R120 -> R240 -> R0
     *
     * R0 永远是该 orbit 的 canonical representative。
     *
     * orbit 中的三个 Complex Shape 都映射到完全相同的
     * ShapeRotations：
     *
     *   _complexRotations[R0.Id]
     *   _complexRotations[R120.Id]
     *   _complexRotations[R240.Id]
     *
     * 三者 value 相等。
     *
     * Inline Shape 不进入 cache；它们的 orbit 可以直接通过
     * ulong bit permutation 计算并 canonicalize。
     */
    private readonly Dictionary<ulong, ShapeRotations>
        _complexRotations = new();

    public static Shape Empty => Shape.Inline(0);

    public static Shape Full => Shape.Inline(ulong.MaxValue);

    public ulong ComplexCount => _complexPool.Count;

    public ShapeStore(ComplexShapePool complexPool) {
        _complexPool = complexPool;
    }

    public ShapeStore()
        : this(new ComplexShapePool()) {
    }

    // ------------------------------------------------------------
    // Expand
    // ------------------------------------------------------------

    public ComplexShapeKey Expand(Shape shape) {
        if (shape.IsComplex)
            return _complexPool.Get(shape);

        ulong bits = shape.InlineBits;

        /*
         * 一个 side-8 inline shape：
         *
         * [ 16 bits ][ 16 bits ][ 16 bits ][ 16 bits ]
         *     C0          C1          C2          C3
         *
         * 每个 child 此时只有 side-4 分辨率。
         *
         * 但所有 Inline Shape 都必须规范成 side-8，
         * 所以把每个 coarse bit refine 成 4 个 bits。
         */

        ushort c0 = (ushort)(bits);
        ushort c1 = (ushort)(bits >> 16);
        ushort c2 = (ushort)(bits >> 32);
        ushort c3 = (ushort)(bits >> 48);

        return new ComplexShapeKey(
            Shape.Inline(Refine16To64(c0)),
            Shape.Inline(Refine16To64(c1)),
            Shape.Inline(Refine16To64(c2)),
            Shape.Inline(Refine16To64(c3)));
    }

    // ------------------------------------------------------------
    // Contract
    // ------------------------------------------------------------

    public Shape Contract(
        Shape c0,
        Shape c1,
        Shape c2,
        Shape c3) {
        var key = new ComplexShapeKey(
            c0,
            c1,
            c2,
            c3);

        return Contract(key);
    }

    public Shape Contract(ComplexShapeKey key) {
        /*
         * 第一优先级永远是尝试 Inline。
         *
         * 只有无法无损压入 side-8 bitmap 时，
         * 才允许创建 Complex Shape。
         */
        if (TryPackInline(key, out ulong bits))
            return Shape.Inline(bits);

        return InternComplexWithRotations(key);
    }

    // ------------------------------------------------------------
    // Rotation
    // ------------------------------------------------------------

    /*
     * ShapeRotations 的语义：
     *
     * R0   = canonical representative
     * R120 = representative + 120°
     * R240 = representative + 240°
     *
     * 因而 GetRotations(shape) 与 queried shape 当前自身的
     * orientation 无关；同一 orbit 内任意 Shape 得到同一个结果。
     */
    public ShapeRotations GetRotations(Shape shape) {
        return shape.IsComplex
            ? GetComplexRotations(shape)
            : GetInlineRotations(shape);
    }

    public Shape GetRepresentative(Shape shape) {
        return GetRotations(shape).R0;
    }

    // ------------------------------------------------------------
    // Item
    // ------------------------------------------------------------

    /*
     * Item identity is Shape modulo 120-degree rotation.
     *
     * The wrapped Shape is always the canonical representative R0.
     */
    public Item ToItem(Shape shape) {
        return new Item(
            GetRepresentative(shape));
    }

    public Shape Rotate120(Shape shape) {
        ShapeRotations rotations =
            GetRotations(shape);

        return RotateCanonical120(
            shape,
            rotations);
    }

    public Shape Rotate240(Shape shape) {
        ShapeRotations rotations =
            GetRotations(shape);

        return RotateCanonical240(
            shape,
            rotations);
    }

    private ShapeRotations GetInlineRotations(
        Shape shape) {
        if (!shape.IsInline) {
            throw new ArgumentException(
                "Shape must be inline.",
                nameof(shape));
        }

        Shape s0 = shape;

        Shape s120 = Shape.Inline(
            RotateInline120(
                s0.InlineBits));

        Shape s240 = Shape.Inline(
            RotateInline120(
                s120.InlineBits));

        /*
         * Inline Shape 的 geometry ordering 可以稳定地直接使用
         * InlineBits。
         *
         * 这里选择 orbit 中 geometry ordering 最大的构象作为
         * representative。
         */
        return CanonicalizeOrbit(
            s0,
            s120,
            s240,
            static (a, b) =>
                a.InlineBits.CompareTo(
                    b.InlineBits));
    }

    private static Shape RotateCanonical120(
        Shape shape,
        ShapeRotations rotations) {
        if (shape == rotations.R0)
            return rotations.R120;

        if (shape == rotations.R120)
            return rotations.R240;

        if (shape == rotations.R240)
            return rotations.R0;

        throw new InvalidOperationException(
            "Shape does not belong to its rotation orbit.");
    }

    private static Shape RotateCanonical240(
        Shape shape,
        ShapeRotations rotations) {
        if (shape == rotations.R0)
            return rotations.R240;

        if (shape == rotations.R120)
            return rotations.R0;

        if (shape == rotations.R240)
            return rotations.R120;

        throw new InvalidOperationException(
            "Shape does not belong to its rotation orbit.");
    }

    /*
     * 输入的 s0/s120/s240 必须已经按实际 +120° rotation 顺序排列：
     *
     *   s0 -> s120 -> s240 -> s0
     *
     * 该方法只负责选择 representative，并保持同一个 rotation
     * direction 重新排列 orbit。
     */
    private static ShapeRotations CanonicalizeOrbit(
        Shape s0,
        Shape s120,
        Shape s240,
        Func<Shape, Shape, int> compare) {
        if (compare(s120, s0) > 0 &&
            compare(s120, s240) >= 0) {
            return new ShapeRotations(
                s120,
                s240,
                s0);
        }

        if (compare(s240, s0) > 0 &&
            compare(s240, s120) > 0) {
            return new ShapeRotations(
                s240,
                s0,
                s120);
        }

        return new ShapeRotations(
            s0,
            s120,
            s240);
    }

    // ------------------------------------------------------------
    // Complex geometry ordering
    // ------------------------------------------------------------

    /*
     * ComplexId 不能用于选择 representative：
     *
     * ComplexId 由 intern 顺序决定，而不是由 shape geometry 决定。
     *
     * 因此 Complex Shape 使用递归的 geometry ordering：
     *
     * 1. Inline vs Inline：比较 InlineBits。
     * 2. Inline vs Complex：定义 Inline < Complex。
     * 3. Complex vs Complex：按 C0,C1,C2,C3 做 lexicographic compare，
     *    每个 child 再递归使用同一 ordering。
     *
     * 这样 representative 只由 shape geometry 决定。
     */
    private int CompareShapeGeometry(
        Shape a,
        Shape b) {
        if (a == b)
            return 0;

        if (a.IsInline && b.IsInline) {
            return a.InlineBits.CompareTo(
                b.InlineBits);
        }

        if (a.IsInline != b.IsInline)
            return a.IsInline ? -1 : 1;

        ComplexShapeKey keyA =
            _complexPool.Get(a);

        ComplexShapeKey keyB =
            _complexPool.Get(b);

        return CompareComplexKey(
            keyA,
            keyB);
    }

    private int CompareComplexKey(
        ComplexShapeKey a,
        ComplexShapeKey b) {
        int cmp =
            CompareShapeGeometry(
                a.C0,
                b.C0);

        if (cmp != 0)
            return cmp;

        cmp =
            CompareShapeGeometry(
                a.C1,
                b.C1);

        if (cmp != 0)
            return cmp;

        cmp =
            CompareShapeGeometry(
                a.C2,
                b.C2);

        if (cmp != 0)
            return cmp;

        return CompareShapeGeometry(
            a.C3,
            b.C3);
    }

    // ------------------------------------------------------------
    // Complex interning
    // ------------------------------------------------------------

    private Shape InternComplexWithRotations(
        ComplexShapeKey key0) {
        /*
         * 注册一个新的 Complex Shape 时，直接生成完整的 exact
         * rotation orbit。
         *
         * 三个 orientation 仍然是三个独立、hashable、interned Shape。
         * 区别只在于：
         *
         * rotation cache 现在将整个 orbit canonicalize，
         * 三个 Shape 都映射到相同 ShapeRotations。
         */

        Shape shape0 =
            _complexPool.Intern(key0);

        /*
         * 如果 shape0 已经属于一个注册过的 orbit，那么该 orbit 的
         * 三个 member 应该都已经注册，无需再次构造。
         */
        if (_complexRotations.ContainsKey(
                shape0.ComplexId)) {
            return shape0;
        }

        ComplexShapeKey key120 =
            RotateKey120(key0);

        ComplexShapeKey key240 =
            RotateKey120(key120);

        /*
         * 如果 key0 无法 Inline，则 rotation 不会改变它所需的最大
         * 细节层级，所以 key120/key240 也无法 Inline。
         */
        Shape shape120 =
            _complexPool.Intern(key120);

        Shape shape240 =
            _complexPool.Intern(key240);

        ShapeRotations canonical =
            CanonicalizeOrbit(
                shape0,
                shape120,
                shape240,
                CompareShapeGeometry);

        RegisterRotationOrbit(
            canonical);

        return shape0;
    }

    private ShapeRotations GetComplexRotations(
        Shape shape) {
        if (!shape.IsComplex) {
            throw new ArgumentException(
                "Shape must be complex.",
                nameof(shape));
        }

        if (_complexRotations.TryGetValue(
                shape.ComplexId,
                out ShapeRotations rotations)) {
            return rotations;
        }

        /*
         * 正常情况下不会发生：
         * 所有 Complex Shape 都应该通过 ShapeStore 创建并在创建时
         * 注册完整的 canonical rotation orbit。
         *
         * 保留恢复逻辑：
         * 从当前 exact orientation 对应的 key 重新生成整个 orbit。
         */
        ComplexShapeKey key =
            _complexPool.Get(shape);

        InternComplexWithRotations(key);

        return _complexRotations[
            shape.ComplexId];
    }

    private void RegisterRotationOrbit(
        ShapeRotations rotations) {
        if (!rotations.R0.IsComplex ||
            !rotations.R120.IsComplex ||
            !rotations.R240.IsComplex) {
            throw new InvalidOperationException(
                "Complex rotation orbit must contain only complex shapes.");
        }

        /*
         * 同一个 canonical ShapeRotations value 注册给 orbit
         * 中的每个 exact Shape。
         */
        SetComplexRotations(
            rotations.R0,
            rotations);

        SetComplexRotations(
            rotations.R120,
            rotations);

        SetComplexRotations(
            rotations.R240,
            rotations);
    }

    private void SetComplexRotations(
        Shape shape,
        ShapeRotations rotations) {
        ulong id =
            shape.ComplexId;

        if (_complexRotations.TryGetValue(
                id,
                out ShapeRotations existing)) {
            if (existing != rotations) {
                throw new InvalidOperationException(
                    $"Inconsistent rotation orbit for {shape}.");
            }

            return;
        }

        _complexRotations.Add(
            id,
            rotations);
    }

    // ------------------------------------------------------------
    // Rotate one ComplexShapeKey
    // ------------------------------------------------------------

    private ComplexShapeKey RotateKey120(
        ComplexShapeKey key) {
        /*
         * Child 编号约定：
         *
         * 三个 corner = 0,1,2
         * center       = 3
         *
         * +120°:
         *
         * source 0 -> target 1
         * source 1 -> target 2
         * source 2 -> target 0
         * source 3 -> target 3
         *
         * 所以反过来看 target：
         *
         * target 0 <- source 2
         * target 1 <- source 0
         * target 2 <- source 1
         * target 3 <- source 3
         *
         * 同时每个 child 自己也必须旋转 120°。
         */

        return new ComplexShapeKey(
            Rotate120(key.C2),
            Rotate120(key.C0),
            Rotate120(key.C1),
            Rotate120(key.C3));
    }

    // ------------------------------------------------------------
    // Inline Contract
    // ------------------------------------------------------------

    private static bool TryPackInline(
        ComplexShapeKey key,
        out ulong result) {
        result = 0;

        /*
         * 只要有一个 Complex child，
         * parent 就不可能压入当前 side-8 inline。
         */
        if (!key.C0.IsInline ||
            !key.C1.IsInline ||
            !key.C2.IsInline ||
            !key.C3.IsInline) {
            return false;
        }

        /*
         * 每个 child 是 side-8。
         *
         * parent 要 side-8 表示的话，
         * child 必须能够无损 coarsen 成 side-4。
         */
        if (!TryCoarsen64To16(
                key.C0.InlineBits,
                out ushort c0)) {
            return false;
        }

        if (!TryCoarsen64To16(
                key.C1.InlineBits,
                out ushort c1)) {
            return false;
        }

        if (!TryCoarsen64To16(
                key.C2.InlineBits,
                out ushort c2)) {
            return false;
        }

        if (!TryCoarsen64To16(
                key.C3.InlineBits,
                out ushort c3)) {
            return false;
        }

        result =
              (ulong)c0
            | ((ulong)c1 << 16)
            | ((ulong)c2 << 32)
            | ((ulong)c3 << 48);

        return true;
    }

    // ------------------------------------------------------------
    // Inline refinement/coarsening
    // ------------------------------------------------------------

    private static ulong Refine16To64(
        ushort coarse) {
        ulong result = 0;

        /*
         * 每个 coarse cell：
         *
         * 0 -> 0000
         * 1 -> 1111
         *
         * 因为 Full 在下一层仍然分成四个 Full。
         */
        for (int i = 0; i < 16; i++) {
            if ((coarse & (1U << i)) != 0) {
                result |=
                    0xFUL << (i * 4);
            }
        }

        return result;
    }

    private static bool TryCoarsen64To16(
        ulong fine,
        out ushort coarse) {
        coarse = 0;

        for (int i = 0; i < 16; i++) {
            uint nibble =
                (uint)((fine >> (i * 4)) & 0xFUL);

            switch (nibble) {
                case 0x0:
                    break;

                case 0xF:
                    coarse |=
                        (ushort)(1U << i);
                    break;

                default:
                    /*
                     * 例如：
                     *
                     * 0011
                     * 0101
                     * 1110
                     *
                     * 都说明这个 coarse cell 内还有细节，
                     * 因此不能无损降一级。
                     */
                    coarse = 0;
                    return false;
            }
        }

        return true;
    }

    // ------------------------------------------------------------
    // Inline rotation
    // ------------------------------------------------------------

    private static readonly byte[] Rotate120Index =
        BuildRotate120Index();

    private static byte[] BuildRotate120Index() {
        var table =
            new byte[64];

        for (int index = 0; index < 64; index++) {
            /*
             * 一个 bit index 本质上就是三个 base-4 digits：
             *
             * index = c0 * 16 + c1 * 4 + c2
             */

            int c0 =
                (index >> 4) & 0b11;

            int c1 =
                (index >> 2) & 0b11;

            int c2 =
                index & 0b11;

            c0 =
                RotateChildIndex120(c0);

            c1 =
                RotateChildIndex120(c1);

            c2 =
                RotateChildIndex120(c2);

            table[index] = (byte)(
                (c0 << 4) |
                (c1 << 2) |
                 c2);
        }

        return table;
    }

    private static int RotateChildIndex120(
        int child) {
        return child switch {
            0 => 1,
            1 => 2,
            2 => 0,
            3 => 3,

            _ => throw new ArgumentOutOfRangeException(
                nameof(child))
        };
    }

    private static ulong RotateInline120(
        ulong bits) {
        ulong result = 0;

        /*
         * 只遍历 occupied bits。
         */
        while (bits != 0) {
            int source =
                BitOperations.TrailingZeroCount(bits);

            bits &=
                bits - 1;

            int target =
                Rotate120Index[source];

            result |=
                1UL << target;
        }

        return result;
    }
}
