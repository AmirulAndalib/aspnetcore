// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;

namespace Microsoft.AspNetCore.Http.Validation;

/// <summary>
/// Represents the context for validating a validatable object.
/// </summary>
[Experimental("ASP0029", UrlFormat = "https://aka.ms/aspnet/analyzer/{0}")]
public sealed class ValidateContext
{
    private JsonNamingPolicy? _cachedNamingPolicy;
    private bool _namingPolicyCached;
    /// <summary>
    /// Gets or sets the validation context used for validating objects that implement <see cref="IValidatableObject"/> or have <see cref="ValidationAttribute"/>.
    /// This context provides access to service provider and other validation metadata.
    /// </summary>
    /// <remarks>
    /// This property should be set by the consumer of the <see cref="IValidatableInfo"/>
    /// interface to provide the necessary context for validation. The object should be initialized
    /// with the current object being validated, the display name, and the service provider to support
    /// the complete set of validation scenarios.
    /// </remarks>
    /// <example>
    /// <code>
    /// var validationContext = new ValidationContext(objectToValidate, serviceProvider, items);
    /// var validationOptions = serviceProvider.GetService&lt;IOptions&lt;ValidationOptions&gt;&gt;()?.Value;
    /// var validateContext = new ValidateContext
    /// {
    ///     ValidationContext = validationContext,
    ///     ValidationOptions = validationOptions
    /// };
    /// </code>
    /// </example>
    public required ValidationContext ValidationContext { get; set; }

    /// <summary>
    /// Gets or sets the prefix used to identify the current object being validated in a complex object graph.
    /// This is used to build property paths in validation error messages (e.g., "Customer.Address.Street").
    /// </summary>
    public string CurrentValidationPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the validation options that control validation behavior,
    /// including validation depth limits and resolver registration.
    /// </summary>
    public required ValidationOptions ValidationOptions { get; set; }

    /// <summary>
    /// Gets or sets the dictionary of validation errors collected during validation.
    /// Keys are property names or paths, and values are arrays of error messages.
    /// In the default implementation, this dictionary is initialized when the first error is added.
    /// </summary>
    public Dictionary<string, string[]>? ValidationErrors { get; set; }

    /// <summary>
    /// Gets or sets the current depth in the validation hierarchy.
    /// This is used to prevent stack overflows from circular references.
    /// </summary>
    public int CurrentDepth { get; set; }
    
    /// <summary>
    /// Gets or sets the JSON serializer options to use for property name formatting.
    /// When available, property names in validation errors will be formatted according to the
    /// PropertyNamingPolicy and JsonPropertyName attributes.
    /// </summary>
    public JsonSerializerOptions? SerializerOptions 
    { 
        get => _serializerOptions;
        set 
        { 
            _serializerOptions = value;
            // Invalidate cache when SerializerOptions changes
            _namingPolicyCached = false;
            _cachedNamingPolicy = null;
        } 
    }
    private JsonSerializerOptions? _serializerOptions;

    /// <summary>
    /// Gets the cached naming policy from SerializerOptions to avoid repeated property access.
    /// </summary>
    private JsonNamingPolicy? CachedNamingPolicy
    {
        get
        {
            if (!_namingPolicyCached)
            {
                _cachedNamingPolicy = _serializerOptions?.PropertyNamingPolicy;
                _namingPolicyCached = true;
            }
            return _cachedNamingPolicy;
        }
    }

    internal void AddValidationError(string key, string[] errors)
    {
        ValidationErrors ??= [];

        var formattedKey = FormatKey(key);
        ValidationErrors[formattedKey] = errors;
    }

    internal void AddOrExtendValidationErrors(string key, string[] errors)
    {
        ValidationErrors ??= [];

        var formattedKey = FormatKey(key);
        
        if (ValidationErrors.TryGetValue(formattedKey, out var existingErrors))
        {
            var newErrors = new string[existingErrors.Length + errors.Length];
            existingErrors.CopyTo(newErrors, 0);
            errors.CopyTo(newErrors, existingErrors.Length);
            ValidationErrors[formattedKey] = newErrors;
        }
        else
        {
            ValidationErrors[formattedKey] = errors;
        }
    }

    internal void AddOrExtendValidationError(string key, string error)
    {
        ValidationErrors ??= [];

        var formattedKey = FormatKey(key);
        
        if (ValidationErrors.TryGetValue(formattedKey, out var existingErrors) && !existingErrors.Contains(error))
        {
            ValidationErrors[formattedKey] = [.. existingErrors, error];
        }
        else
        {
            ValidationErrors[formattedKey] = [error];
        }
    }
    
    private string FormatKey(string key)
    {
        var namingPolicy = CachedNamingPolicy;
        if (string.IsNullOrEmpty(key) || namingPolicy is null)
        {
            return key;
        }

        // If the key contains a path (e.g., "Address.Street" or "Items[0].Name"), 
        // apply the naming policy to each part of the path
        if (key.Contains('.') || key.Contains('['))
        {
            return FormatComplexKey(key, namingPolicy);
        }

        // Apply the naming policy directly
        return namingPolicy.ConvertName(key);
    }
    
    private static string FormatComplexKey(string key, JsonNamingPolicy namingPolicy)
    {
        // Use a more direct approach for complex keys with dots and array indices
        var result = new System.Text.StringBuilder();
        int lastIndex = 0;
        int i = 0;
        bool inBracket = false;

        while (i < key.Length)
        {
            char c = key[i];
            
            if (c == '[')
            {
                // Format the segment before the bracket
                if (i > lastIndex)
                {
                    string segment = key.Substring(lastIndex, i - lastIndex);
                    string formattedSegment = namingPolicy.ConvertName(segment);
                    result.Append(formattedSegment);
                }
                
                // Start collecting the bracket part
                inBracket = true;
                result.Append(c);
                lastIndex = i + 1;
            }
            else if (c == ']')
            {
                // Add the content inside the bracket as-is
                if (i > lastIndex)
                {
                    string segment = key.Substring(lastIndex, i - lastIndex);
                    result.Append(segment);
                }
                result.Append(c);
                inBracket = false;
                lastIndex = i + 1;
            }
            else if (c == '.' && !inBracket)
            {
                // Format the segment before the dot
                if (i > lastIndex)
                {
                    string segment = key.Substring(lastIndex, i - lastIndex);
                    string formattedSegment = namingPolicy.ConvertName(segment);
                    result.Append(formattedSegment);
                }
                result.Append(c);
                lastIndex = i + 1;
            }

            i++;
        }

        // Format the last segment if there is one
        if (lastIndex < key.Length)
        {
            string segment = key.Substring(lastIndex);
            if (!inBracket)
            {
                segment = namingPolicy.ConvertName(segment);
            }
            result.Append(segment);
        }

        return result.ToString();
    }
    

}
