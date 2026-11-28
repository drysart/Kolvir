namespace Kolvir.Tests;

/// <summary>
/// A style is immutable and compares by what it says, which is what lets the table decide that a
/// newly built sequence of styles means the same as the one already on it.
/// </summary>
[TestClass]
[TestCategory("Core")]
public sealed class RowOrColumnStyleTests
{
    [TestMethod]
    public void AStyleBuiltInAnInitializerEqualsTheSameStyleFromAConstructor()
    {
        var constructed = new RowStyle(SizeType.Absolute, 40f);
        var initialized = new RowStyle
        {
            SizeType = SizeType.Absolute,
            Value = 40f,
        };

        Assert.AreEqual(constructed, initialized);
        Assert.IsTrue(constructed == initialized, "the operator agrees with Equals");
        Assert.AreEqual(constructed.GetHashCode(), initialized.GetHashCode(), "equal styles hash alike");
    }

    [TestMethod]
    public void ADifferentSizeTypeIsADifferentStyle()
    {
        var pixels = new ColumnStyle(SizeType.Absolute, 40f);
        var percent = new ColumnStyle(SizeType.Percent, 40f);

        Assert.AreNotEqual<RowOrColumnStyle>(pixels, percent);
        Assert.IsTrue(pixels != percent);
    }

    [TestMethod]
    public void ADifferentValueIsADifferentStyle()
    {
        var narrow = new ColumnStyle(SizeType.Percent, 25f);
        var wide = new ColumnStyle(SizeType.Percent, 75f);

        Assert.AreNotEqual<RowOrColumnStyle>(narrow, wide);
        Assert.IsFalse(narrow.Equals(wide));
    }

    [TestMethod]
    public void ARowNeverEqualsAColumn()
    {
        var row = new RowStyle(SizeType.Absolute, 40f);
        var column = new ColumnStyle(SizeType.Absolute, 40f);

        // One is a height and the other a width: the numbers agreeing does not make them the same
        // instruction, and the table compare must not think a row sequence became a column one.
        Assert.AreNotEqual<RowOrColumnStyle>(row, column);
    }

    [TestMethod]
    public void ARowThatAlignsItsBaselinesIsADifferentStyle()
    {
        var plain = new RowStyle(SizeType.AutoSize, 0f);
        var aligned = new RowStyle(SizeType.AutoSize, 0f) { AlignBaselines = true };

        // The table drops an assignment that says what it already says, so a difference this equality
        // did not see would be a switch the table never applied.
        Assert.AreNotEqual(plain, aligned);
        Assert.AreNotEqual(plain.GetHashCode(), aligned.GetHashCode(), "and the hash agrees with it");
        Assert.IsTrue(plain != aligned);
        Assert.AreEqual(aligned, new RowStyle(SizeType.AutoSize, 0f) { AlignBaselines = true });
    }

    [TestMethod]
    public void AssigningAnAlignedRowAfterAPlainOneIsAPass()
    {
        using var table = new CountingTable { Padding = Thickness.Empty, CellPadding = Thickness.Empty };
        table.RowStyles = [new RowStyle(SizeType.AutoSize, 0f)];

        int passes = table.Passes;

        table.RowStyles = [new RowStyle(SizeType.AutoSize, 0f) { AlignBaselines = true }];

        Assert.AreEqual(passes + 1, table.Passes, "turning the row's alignment on is a change");
        Assert.IsTrue(table.RowStyles[0].AlignBaselines);
    }

    [TestMethod]
    public void ANullIsNotAStyle()
    {
        var style = new RowStyle(SizeType.Absolute, 40f);

        Assert.IsFalse(style.Equals(null));
        Assert.IsFalse(style == null);
        Assert.IsTrue((RowOrColumnStyle?)null == null);
        Assert.IsTrue(style != null, "a style is not null, so it differs from null");
    }

    [TestMethod]
    public void TheFactoriesAndTheConstructorsAgree()
    {
        Assert.AreEqual(new RowStyle(SizeType.AutoSize, 0f), RowStyle.AutoSize());
        Assert.AreEqual(new RowStyle(SizeType.Absolute, 30f), RowStyle.Absolute(30f));
        Assert.AreEqual(new RowStyle(SizeType.Percent, 50f), RowStyle.Percent(50f));
        Assert.AreEqual(new ColumnStyle(SizeType.Absolute, 30f), ColumnStyle.Absolute(30f));
    }

    /// <summary>A table that counts the layout passes it runs.</summary>
    private sealed class CountingTable : TableLayoutPanel
    {
        internal int Passes { get; private set; }

        protected override void OnLayout()
        {
            Passes++;
            base.OnLayout();
        }
    }

    [TestMethod]
    public void AValueThatCannotBeASizeIsClampedWhenTheStyleIsBuilt()
    {
        // The clamp is where a style is built now that there is no setter, so it has to survive the
        // constructor and the initializer alike.
        Assert.AreEqual(0f, new RowStyle(SizeType.Absolute, -5f).Value);
        Assert.AreEqual(0f, new RowStyle(SizeType.Absolute, float.NaN).Value);
        Assert.AreEqual(0f, new RowStyle { SizeType = SizeType.Absolute, Value = -5f }.Value);
    }
}
