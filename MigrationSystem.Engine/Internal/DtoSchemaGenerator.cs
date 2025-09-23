using MigrationSystem.Core.Validation;
using NJsonSchema;
using NJsonSchema.Generation;
using System;
using System.Reflection;

namespace MigrationSystem.Engine.Internal;

/// <summary>
/// Generates a JsonSchema from a DTO's Type definition and custom attributes.
/// </summary>
internal class DtoSchemaGenerator
{
    public JsonSchema GenerateSchemaForType(Type dtoType)
    {
        var settings = new SystemTextJsonSchemaGeneratorSettings();
        var generator = new JsonSchemaGenerator(settings);
        var schema = generator.Generate(dtoType);

        // Post-process the schema to apply our custom validation attributes.
        // A more advanced implementation might use NJsonSchema's SchemaProcessors.
        ApplyCustomAttributes(dtoType, schema);

        return schema;
    }

    private void ApplyCustomAttributes(Type type, JsonSchema schema)
    {
        var descriptionAttr = type.GetCustomAttribute<SchemaDescriptionAttribute>();
        if (descriptionAttr != null)
        {
            schema.Description = descriptionAttr.Description;
        }

        foreach (var property in type.GetProperties())
        {
            if (!schema.Properties.TryGetValue(property.Name, out var schemaProperty))
            {
                // NJsonSchema might use a different name casing strategy.
                // This is a simplified lookup.
                var jsonPropertyName = property.Name.ToLowerInvariant();  
                schema.Properties.TryGetValue(jsonPropertyName, out schemaProperty);
            }

            if (schemaProperty != null)
            {
                ApplyPropertyAttributes(property, schemaProperty);
            }
        }
    }

    private void ApplyPropertyAttributes(PropertyInfo property, JsonSchemaProperty schemaProperty)
    {
        var descriptionAttr = property.GetCustomAttribute<SchemaDescriptionAttribute>();
        if (descriptionAttr != null) schemaProperty.Description = descriptionAttr.Description;

        var rangeAttr = property.GetCustomAttribute<NumberRangeAttribute>();
        if (rangeAttr != null)
        {
            schemaProperty.Minimum = (decimal)rangeAttr.Minimum;
            schemaProperty.Maximum = (decimal)rangeAttr.Maximum;
        }

        var patternAttr = property.GetCustomAttribute<StringPatternAttribute>();
        if (patternAttr != null) schemaProperty.Pattern = patternAttr.Pattern;

        var arrayAttr = property.GetCustomAttribute<ArrayCountAttribute>();
        if (arrayAttr != null)
        {
            schemaProperty.MinItems = arrayAttr.MinItems;
            if (arrayAttr.MaxItems > -1) schemaProperty.MaxItems = arrayAttr.MaxItems;
        }
    }
}