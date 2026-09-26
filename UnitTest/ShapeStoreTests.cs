public sealed class ShapeStoreTests {
    private readonly ShapeStore _store = new();

    // ------------------------------------------------------------
    // Basic Inline shapes
    // ------------------------------------------------------------

    [Fact]
    public void Empty_ExpandToFourEmptyShapes() {
        var children = _store.Expand(ShapeStore.Empty);

        Assert.Equal(ShapeStore.Empty, children.C0);
        Assert.Equal(ShapeStore.Empty, children.C1);
        Assert.Equal(ShapeStore.Empty, children.C2);
        Assert.Equal(ShapeStore.Empty, children.C3);
    }

    [Fact]
    public void Full_ExpandToFourFullShapes() {
        var children = _store.Expand(ShapeStore.Full);

        Assert.Equal(ShapeStore.Full, children.C0);
        Assert.Equal(ShapeStore.Full, children.C1);
        Assert.Equal(ShapeStore.Full, children.C2);
        Assert.Equal(ShapeStore.Full, children.C3);
    }

    [Fact]
    public void ContractFourEmptyShapes_ReturnsEmpty() {
        Shape result = _store.Contract(
            ShapeStore.Empty,
            ShapeStore.Empty,
            ShapeStore.Empty,
            ShapeStore.Empty);

        Assert.Equal(ShapeStore.Empty, result);
        Assert.True(result.IsInline);
    }

    [Fact]
    public void ContractFourFullShapes_ReturnsFull() {
        Shape result = _store.Contract(
            ShapeStore.Full,
            ShapeStore.Full,
            ShapeStore.Full,
            ShapeStore.Full);

        Assert.Equal(ShapeStore.Full, result);
        Assert.True(result.IsInline);
    }

    // ------------------------------------------------------------
    // Expand / Contract
    // ------------------------------------------------------------

    [Theory]
    [InlineData(0x0000000000000000UL)]
    [InlineData(0xFFFFFFFFFFFFFFFFUL)]
    [InlineData(0x0000000000000001UL)]
    [InlineData(0x8000000000000000UL)]
    [InlineData(0x0123456789ABCDEFUL)]
    [InlineData(0xAAAAAAAAAAAAAAAAUL)]
    [InlineData(0x5555555555555555UL)]
    public void Inline_ContractExpand_IsIdentity(ulong bits) {
        Shape original = Shape.Inline(bits);

        ComplexShapeKey children =
            _store.Expand(original);

        Shape restored =
            _store.Contract(children);

        Assert.Equal(original, restored);
        Assert.True(restored.IsInline);
    }

    [Fact]
    public void Complex_ContractExpand_IsIdentity() {
        Shape detailedChild =
            Shape.Inline(0x1UL);

        /*
         * 0x1 不能从 side-8 无损 coarsen 到 side-4，
         * 因为最低 nibble 是 0001。
         *
         * 因此 parent 必须成为 Complex。
         */
        Shape original = _store.Contract(
            detailedChild,
            ShapeStore.Empty,
            ShapeStore.Empty,
            ShapeStore.Empty);

        Assert.True(original.IsComplex);

        ComplexShapeKey children =
            _store.Expand(original);

        Shape restored =
            _store.Contract(children);

        Assert.Equal(original, restored);
        Assert.True(restored.IsComplex);
    }

    // ------------------------------------------------------------
    // Inline / Complex boundary
    // ------------------------------------------------------------

    [Fact]
    public void Contract_CoarsenableChildren_UsesInline() {
        /*
         * 每个 nibble 要么 0000，要么 1111，
         * 因而这个 child 可以无损 coarsen。
         */
        Shape child =
            Shape.Inline(0x000000000000000FUL);

        Shape result = _store.Contract(
            child,
            ShapeStore.Empty,
            ShapeStore.Empty,
            ShapeStore.Empty);

        Assert.True(result.IsInline);
    }

    [Fact]
    public void Contract_NonCoarsenableChild_UsesComplex() {
        /*
         * nibble = 0001
         * 无法压成一个 coarse Full/Empty bit。
         */
        Shape child =
            Shape.Inline(0x1UL);

        Shape result = _store.Contract(
            child,
            ShapeStore.Empty,
            ShapeStore.Empty,
            ShapeStore.Empty);

        Assert.True(result.IsComplex);
    }

    [Fact]
    public void SameComplexStructure_IsInterned() {
        Shape child =
            Shape.Inline(0x1UL);

        Shape a = _store.Contract(
            child,
            ShapeStore.Empty,
            ShapeStore.Empty,
            ShapeStore.Empty);

        Shape b = _store.Contract(
            child,
            ShapeStore.Empty,
            ShapeStore.Empty,
            ShapeStore.Empty);

        Assert.True(a.IsComplex);
        Assert.True(b.IsComplex);

        Assert.Equal(a, b);
        Assert.Equal(a.ComplexId, b.ComplexId);
    }

    // ------------------------------------------------------------
    // Rotation
    // ------------------------------------------------------------

    [Theory]
    [InlineData(0x0000000000000000UL)]
    [InlineData(0xFFFFFFFFFFFFFFFFUL)]
    [InlineData(0x1UL)]
    [InlineData(0x0123456789ABCDEFUL)]
    [InlineData(0xAAAAAAAAAAAAAAAAUL)]
    public void Inline_RotateThreeTimes_IsIdentity(
        ulong bits) {
        Shape original = Shape.Inline(bits);

        Shape r120 =
            _store.Rotate120(original);

        Shape r240 =
            _store.Rotate120(r120);

        Shape r360 =
            _store.Rotate120(r240);

        Assert.Equal(original, r360);
    }

    [Fact]
    public void Full_IsRotationInvariant() {
        Assert.Equal(
            ShapeStore.Full,
            _store.Rotate120(ShapeStore.Full));

        Assert.Equal(
            ShapeStore.Full,
            _store.Rotate240(ShapeStore.Full));
    }

    [Fact]
    public void Empty_IsRotationInvariant() {
        Assert.Equal(
            ShapeStore.Empty,
            _store.Rotate120(ShapeStore.Empty));

        Assert.Equal(
            ShapeStore.Empty,
            _store.Rotate240(ShapeStore.Empty));
    }

    [Fact]
    public void Complex_RotateThreeTimes_IsIdentity() {
        Shape detailedChild =
            Shape.Inline(0x1UL);

        Shape original = _store.Contract(
            detailedChild,
            ShapeStore.Empty,
            ShapeStore.Empty,
            ShapeStore.Empty);

        Assert.True(original.IsComplex);

        Shape r120 =
            _store.Rotate120(original);

        Shape r240 =
            _store.Rotate120(r120);

        Shape r360 =
            _store.Rotate120(r240);

        Assert.Equal(original, r360);
    }

    // ------------------------------------------------------------
    // Rotation must commute with Expand
    // ------------------------------------------------------------

    [Theory]
    [InlineData(0x0123456789ABCDEFUL)]
    [InlineData(0x000F00F00F000FF0UL)]
    public void Inline_RotateThenExpand_EqualsExpandThenRotateChildren(
        ulong bits) {
        AssertRotationAndExpandConsistency(
            Shape.Inline(bits));
    }

    [Fact]
    public void Complex_RotateThenExpand_EqualsExpandThenRotateChildren() {
        Shape child0 =
            Shape.Inline(0x1UL);

        Shape child1 =
            Shape.Inline(0x10UL);

        Shape original = _store.Contract(
            child0,
            child1,
            ShapeStore.Empty,
            ShapeStore.Empty);

        Assert.True(original.IsComplex);

        AssertRotationAndExpandConsistency(original);
    }

    private void AssertRotationAndExpandConsistency(
        Shape shape) {
        ComplexShapeKey original =
            _store.Expand(shape);

        Shape rotatedShape =
            _store.Rotate120(shape);

        ComplexShapeKey actual =
            _store.Expand(rotatedShape);

        /*
         * 与 ShapeStore.RotateKey120() 使用完全相同的
         * 几何 convention：
         *
         * target 0 <- source 2
         * target 1 <- source 0
         * target 2 <- source 1
         * target 3 <- source 3
         */
        var expected = new ComplexShapeKey(
            _store.Rotate120(original.C2),
            _store.Rotate120(original.C0),
            _store.Rotate120(original.C1),
            _store.Rotate120(original.C3));

        Assert.Equal(expected, actual);
    }


    [Fact]
    public void ComplexRotation_IsStableAcrossRepeatedQueries() {
        Shape detailedChild =
            Shape.Inline(0x1UL);

        Shape shape = _store.Contract(
            detailedChild,
            ShapeStore.Empty,
            ShapeStore.Empty,
            ShapeStore.Empty);

        Shape first =
            _store.Rotate120(shape);

        Shape second =
            _store.Rotate120(shape);

        Assert.Equal(first, second);

        Assert.True(first.IsComplex);
        Assert.Equal(
            first.ComplexId,
            second.ComplexId);
    }
    [Fact]
    public void Inline_GetRotations_ReturnsSameCanonicalOrbit_ForEveryRotation() {
        var store = new ShapeStore();

        Shape shape = Shape.Inline(
            0x0000_0000_0000_0001UL);

        ShapeRotations rotations =
            store.GetRotations(shape);

        ShapeRotations fromR0 =
            store.GetRotations(rotations.R0);

        ShapeRotations fromR120 =
            store.GetRotations(rotations.R120);

        ShapeRotations fromR240 =
            store.GetRotations(rotations.R240);

        Assert.Equal(
            rotations,
            fromR0);
        Assert.Equal(
            rotations,
            fromR120);
        Assert.Equal(
            rotations,
            fromR240);
    }

    [Fact]
    public void Inline_GetRotations_R0IsMaximumInlineBitsRepresentative() {
        var store = new ShapeStore();

        Shape shape = Shape.Inline(
            0x0000_0000_0000_0001UL);

        ShapeRotations rotations =
            store.GetRotations(shape);

        ulong expected =
            Math.Max(
                rotations.R0.InlineBits,
                Math.Max(
                    rotations.R120.InlineBits,
                    rotations.R240.InlineBits));

        Assert.Equal(
            expected,
            rotations.R0.InlineBits);
    }

    [Fact]
    public void Inline_GetRepresentative_IsRotationInvariant() {
        var store = new ShapeStore();

        Shape shape = Shape.Inline(
            0x0123_4567_89AB_CDEFUL);

        Shape r120 =
            store.Rotate120(shape);

        Shape r240 =
            store.Rotate240(shape);

        Shape representative =
            store.GetRepresentative(shape);

        Assert.Equal(
            representative,
            store.GetRepresentative(r120));

        Assert.Equal(
            representative,
            store.GetRepresentative(r240));
    }

    [Fact]
    public void Inline_Rotate120_FollowsCanonicalOrbit() {
        var store = new ShapeStore();

        Shape shape = Shape.Inline(
            0x0000_0000_0000_0001UL);

        ShapeRotations rotations =
            store.GetRotations(shape);

        Assert.Equal(
            rotations.R120,
            store.Rotate120(rotations.R0));

        Assert.Equal(
            rotations.R240,
            store.Rotate120(rotations.R120));

        Assert.Equal(
            rotations.R0,
            store.Rotate120(rotations.R240));
    }

    [Fact]
    public void Inline_Rotate240_FollowsCanonicalOrbitBackwards() {
        var store = new ShapeStore();

        Shape shape = Shape.Inline(
            0x0000_0000_0000_0001UL);

        ShapeRotations rotations =
            store.GetRotations(shape);

        Assert.Equal(
            rotations.R240,
            store.Rotate240(rotations.R0));

        Assert.Equal(
            rotations.R0,
            store.Rotate240(rotations.R120));

        Assert.Equal(
            rotations.R120,
            store.Rotate240(rotations.R240));
    }

    [Fact]
    public void Inline_Rotate120_ThreeTimes_ReturnsOriginalExactShape() {
        var store = new ShapeStore();

        Shape original = Shape.Inline(
            0x0123_4567_89AB_CDEFUL);

        Shape rotated =
            store.Rotate120(
                store.Rotate120(
                    store.Rotate120(original)));

        Assert.Equal(
            original,
            rotated);
    }

    [Fact]
    public void Inline_Rotate120And240_AreInverses() {
        var store = new ShapeStore();

        Shape original = Shape.Inline(
            0x0123_4567_89AB_CDEFUL);

        Assert.Equal(
            original,
            store.Rotate240(
                store.Rotate120(original)));

        Assert.Equal(
            original,
            store.Rotate120(
                store.Rotate240(original)));
    }

    [Fact]
    public void Inline_RotationallySymmetricShapes_HaveSingleOrbitMember() {
        var store = new ShapeStore();

        Shape[] shapes = {
            ShapeStore.Empty,
            ShapeStore.Full
        };

        foreach (Shape shape in shapes) {
            ShapeRotations rotations =
                store.GetRotations(shape);

            Assert.Equal(
                shape,
                rotations.R0);

            Assert.Equal(
                shape,
                rotations.R120);

            Assert.Equal(
                shape,
                rotations.R240);

            Assert.Equal(
                shape,
                store.Rotate120(shape));

            Assert.Equal(
                shape,
                store.Rotate240(shape));
        }
    }

    // ------------------------------------------------------------
    // Complex canonical rotation orbit
    // ------------------------------------------------------------

    [Fact]
    public void Complex_GetRotations_ReturnsSameCanonicalOrbit_ForEveryRotation() {
        var store =
            new ShapeStore();

        Shape shape =
            CreateAsymmetricComplex(store);

        Assert.True(
            shape.IsComplex);

        ShapeRotations rotations =
            store.GetRotations(shape);

        Assert.True(
            rotations.R0.IsComplex);

        Assert.True(
            rotations.R120.IsComplex);

        Assert.True(
            rotations.R240.IsComplex);

        Assert.Equal(
            rotations,
            store.GetRotations(rotations.R0));

        Assert.Equal(
            rotations,
            store.GetRotations(rotations.R120));

        Assert.Equal(
            rotations,
            store.GetRotations(rotations.R240));
    }

    [Fact]
    public void Complex_GetRepresentative_IsRotationInvariant() {
        var store =
            new ShapeStore();

        Shape shape =
            CreateAsymmetricComplex(store);

        ShapeRotations rotations =
            store.GetRotations(shape);

        Shape representative =
            rotations.R0;

        Assert.Equal(
            representative,
            store.GetRepresentative(rotations.R0));

        Assert.Equal(
            representative,
            store.GetRepresentative(rotations.R120));

        Assert.Equal(
            representative,
            store.GetRepresentative(rotations.R240));
    }

    [Fact]
    public void Complex_Rotate120_FollowsCanonicalOrbit() {
        var store =
            new ShapeStore();

        Shape shape =
            CreateAsymmetricComplex(store);

        ShapeRotations rotations =
            store.GetRotations(shape);

        Assert.Equal(
            rotations.R120,
            store.Rotate120(rotations.R0));

        Assert.Equal(
            rotations.R240,
            store.Rotate120(rotations.R120));

        Assert.Equal(
            rotations.R0,
            store.Rotate120(rotations.R240));
    }

    [Fact]
    public void Complex_Rotate240_FollowsCanonicalOrbitBackwards() {
        var store =
            new ShapeStore();

        Shape shape =
            CreateAsymmetricComplex(store);

        ShapeRotations rotations =
            store.GetRotations(shape);

        Assert.Equal(
            rotations.R240,
            store.Rotate240(rotations.R0));

        Assert.Equal(
            rotations.R0,
            store.Rotate240(rotations.R120));

        Assert.Equal(
            rotations.R120,
            store.Rotate240(rotations.R240));
    }

    [Fact]
    public void Complex_Rotate120_ThreeTimes_ReturnsOriginalExactShape() {
        var store =
            new ShapeStore();

        Shape original =
            CreateAsymmetricComplex(store);

        Shape rotated =
            store.Rotate120(
                store.Rotate120(
                    store.Rotate120(original)));

        Assert.Equal(
            original,
            rotated);
    }

    [Fact]
    public void Complex_Rotate120And240_AreInverses() {
        var store =
            new ShapeStore();

        Shape original =
            CreateAsymmetricComplex(store);

        Assert.Equal(
            original,
            store.Rotate240(
                store.Rotate120(original)));

        Assert.Equal(
            original,
            store.Rotate120(
                store.Rotate240(original)));
    }

    /*
     * 这是此次 canonical representative 设计最重要的 regression test
     * 之一。
     *
     * 两个独立 ShapeStore 以不同的 orientation 作为第一次 intern
     * 的 Complex Shape，因此 ComplexId 分配顺序不同。
     *
     * representative 仍必须具有相同 geometry。
     *
     * 如果 representative 错误地依赖 ComplexId，这个测试就有机会
     * 暴露问题。
     */
    [Fact]
    public void Complex_Representative_DoesNotDependOnInternOrder() {
        var storeA =
            new ShapeStore();

        ComplexShapeKey keyA =
            CreateAsymmetricComplexKey();

        Shape shapeA =
            storeA.Contract(keyA);

        Assert.True(
            shapeA.IsComplex);

        string representativeA =
            GeometrySignature(
                storeA,
                storeA.GetRepresentative(shapeA));


        var storeB =
            new ShapeStore();

        ComplexShapeKey keyB =
            CreateAsymmetricComplexKey();

        /*
         * 先构造 +120° orientation，让 storeB 的 complex IDs
         * 按不同 orientation order 被分配。
         */
        ComplexShapeKey rotatedKeyB =
            RotateKey120ForTest(
                storeB,
                keyB);

        Shape rotatedFirst =
            storeB.Contract(rotatedKeyB);

        Assert.True(
            rotatedFirst.IsComplex);

        /*
         * 再请求原始 orientation。
         */
        Shape shapeB =
            storeB.Contract(keyB);

        string representativeB =
            GeometrySignature(
                storeB,
                storeB.GetRepresentative(shapeB));

        Assert.Equal(
            representativeA,
            representativeB);
    }

    [Fact]
    public void Complex_AllOrbitMembers_HaveSameRepresentativeGeometry() {
        var store =
            new ShapeStore();

        Shape shape =
            CreateAsymmetricComplex(store);

        ShapeRotations rotations =
            store.GetRotations(shape);

        string expected =
            GeometrySignature(
                store,
                rotations.R0);

        Assert.Equal(
            expected,
            GeometrySignature(
                store,
                store.GetRepresentative(rotations.R0)));

        Assert.Equal(
            expected,
            GeometrySignature(
                store,
                store.GetRepresentative(rotations.R120)));

        Assert.Equal(
            expected,
            GeometrySignature(
                store,
                store.GetRepresentative(rotations.R240)));
    }

    // ------------------------------------------------------------
    // Contract / rotation integration
    // ------------------------------------------------------------

    [Fact]
    public void ContractingRotatedComplexKey_ProducesSameCanonicalOrbit() {
        var store =
            new ShapeStore();

        ComplexShapeKey key =
            CreateAsymmetricComplexKey();

        Shape s0 =
            store.Contract(key);

        ComplexShapeKey key120 =
            RotateKey120ForTest(
                store,
                key);

        Shape s120 =
            store.Contract(key120);

        Assert.True(
            s0.IsComplex);

        Assert.True(
            s120.IsComplex);

        ShapeRotations rotations0 =
            store.GetRotations(s0);

        ShapeRotations rotations120 =
            store.GetRotations(s120);

        Assert.Equal(
            rotations0,
            rotations120);

        Assert.Equal(
            store.GetRepresentative(s0),
            store.GetRepresentative(s120));
    }

    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------

    /*
     * 0x1 这样的 inline shape 无法 TryCoarsen64To16：
     *
     * 第一个 nibble = 0001，而不是 0000/1111。
     *
     * 因此 Contract 这个 key 必然生成 Complex Shape。
     * 同时四个 child 不同，确保整体不是平凡 rotationally
     * symmetric geometry。
     */
    private static ComplexShapeKey CreateAsymmetricComplexKey() {
        return new ComplexShapeKey(
            Shape.Inline(
                0x0000_0000_0000_0001UL),

            Shape.Inline(
                0x0000_0000_0000_0010UL),

            Shape.Inline(
                0x0000_0000_0000_0100UL),

            Shape.Inline(
                0x0000_0000_0000_1000UL));
    }

    private static Shape CreateAsymmetricComplex(
        ShapeStore store) {
        Shape shape =
            store.Contract(
                CreateAsymmetricComplexKey());

        if (!shape.IsComplex) {
            throw new InvalidOperationException(
                "Test fixture expected a Complex Shape.");
        }

        return shape;
    }

    /*
     * 与 ShapeStore.RotateKey120 的 geometry rule 保持一致，
     * 但只使用 public API，以便从测试侧构造某个 ComplexShapeKey
     * 的 exact +120° orientation。
     */
    private static ComplexShapeKey RotateKey120ForTest(
        ShapeStore store,
        ComplexShapeKey key) {
        return new ComplexShapeKey(
            store.Rotate120(key.C2),
            store.Rotate120(key.C0),
            store.Rotate120(key.C1),
            store.Rotate120(key.C3));
    }

    /*
     * 跨 ShapeStore 不能直接比较 Complex Shape：
     * ComplexId 是 store-local identity。
     *
     * 因此把 geometry 递归展开成一个稳定 signature。
     */
    private static string GeometrySignature(
        ShapeStore store,
        Shape shape) {
        if (shape.IsInline) {
            return $"I:{shape.InlineBits:X16}";
        }

        ComplexShapeKey key =
            store.Expand(shape);

        return
            "C(" +
            GeometrySignature(store, key.C0) + "," +
            GeometrySignature(store, key.C1) + "," +
            GeometrySignature(store, key.C2) + "," +
            GeometrySignature(store, key.C3) +
            ")";
    }
}