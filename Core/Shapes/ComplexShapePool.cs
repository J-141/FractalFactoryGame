using System;
using System.Collections.Generic;

public sealed class ComplexShapePool {
    private readonly List<ComplexShapeKey> _nodes = new();

    private readonly Dictionary<ComplexShapeKey, ulong> _intern = new();

    public ulong Count => (ulong)_nodes.Count;

    /// <summary>
    /// 返回这个 exact complex structure 的唯一 Shape。
    ///
    /// 如果完全相同的 (C0,C1,C2,C3) 已存在，
    /// 返回已有 ComplexId。
    /// </summary>
    public Shape Intern(
        Shape c0,
        Shape c1,
        Shape c2,
        Shape c3) {
        return Intern(
            new ComplexShapeKey(c0, c1, c2, c3));
    }

    public Shape Intern(ComplexShapeKey key) {
        if (_intern.TryGetValue(key, out ulong existingId))
            return Shape.Complex(existingId);

        ulong id = (ulong)_nodes.Count;

        _nodes.Add(key);
        _intern.Add(key, id);

        return Shape.Complex(id);
    }

    /// <summary>
    /// 根据 Complex Shape 取得四个 child。
    /// </summary>
    public ComplexShapeKey Get(Shape shape) {
        if (!shape.IsComplex) {
            throw new ArgumentException(
                "Shape must be complex.",
                nameof(shape));
        }

        return Get(shape.ComplexId);
    }

    /// <summary>
    /// 根据 ComplexId 取得四个 child。
    /// </summary>
    public ComplexShapeKey Get(ulong id) {
        if (id >= (ulong)_nodes.Count) {
            throw new ArgumentOutOfRangeException(
                nameof(id),
                $"Complex shape id {id} does not exist.");
        }

        return _nodes[checked((int)id)];
    }

    public bool TryGet(
        ulong id,
        out ComplexShapeKey key) {
        if (id >= (ulong)_nodes.Count) {
            key = default;
            return false;
        }

        key = _nodes[(int)id];
        return true;
    }
}