using System;

namespace MigrationSystem.Core.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public class SchemaDescriptionAttribute(string description) : Attribute
{
    public string Description { get; } = description;
}

[AttributeUsage(AttributeTargets.Property)]
public class NumberRangeAttribute(double minimum, double maximum) : Attribute
{
    public double Minimum { get; } = minimum;
    public double Maximum { get; } = maximum;
}

[AttributeUsage(AttributeTargets.Property)]
public class StringPatternAttribute(string pattern) : Attribute
{
    public string Pattern { get; } = pattern;
}

[AttributeUsage(AttributeTargets.Property)]
public class ArrayCountAttribute(int minItems, int maxItems = -1) : Attribute
{
    public int MinItems { get; } = minItems;
    public int MaxItems { get; } = maxItems;
}