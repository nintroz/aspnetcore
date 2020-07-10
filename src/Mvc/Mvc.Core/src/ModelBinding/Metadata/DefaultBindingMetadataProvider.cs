// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Microsoft.AspNetCore.Mvc.ModelBinding.Metadata
{
    /// <summary>
    /// A default implementation of <see cref="IBindingMetadataProvider"/>.
    /// </summary>
    internal class DefaultBindingMetadataProvider : IBindingMetadataProvider
    {
        public void CreateBindingMetadata(BindingMetadataProviderContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // BinderModelName
            foreach (var binderModelNameAttribute in context.Attributes.OfType<IModelNameProvider>())
            {
                if (binderModelNameAttribute?.Name != null)
                {
                    context.BindingMetadata.BinderModelName = binderModelNameAttribute.Name;
                    break;
                }
            }

            // BinderType
            foreach (var binderTypeAttribute in context.Attributes.OfType<IBinderTypeProviderMetadata>())
            {
                if (binderTypeAttribute.BinderType != null)
                {
                    context.BindingMetadata.BinderType = binderTypeAttribute.BinderType;
                    break;
                }
            }

            // BindingSource
            foreach (var bindingSourceAttribute in context.Attributes.OfType<IBindingSourceMetadata>())
            {
                if (bindingSourceAttribute.BindingSource != null)
                {
                    context.BindingMetadata.BindingSource = bindingSourceAttribute.BindingSource;
                    break;
                }
            }

            // PropertyFilterProvider
            var propertyFilterProviders = context.Attributes.OfType<IPropertyFilterProvider>().ToArray();
            if (propertyFilterProviders.Length == 0)
            {
                context.BindingMetadata.PropertyFilterProvider = null;
            }
            else if (propertyFilterProviders.Length == 1)
            {
                context.BindingMetadata.PropertyFilterProvider = propertyFilterProviders[0];
            }
            else
            {
                var composite = new CompositePropertyFilterProvider(propertyFilterProviders);
                context.BindingMetadata.PropertyFilterProvider = composite;
            }

            var bindingBehavior = FindBindingBehavior(context);
            if (bindingBehavior != null)
            {
                context.BindingMetadata.IsBindingAllowed = bindingBehavior.Behavior != BindingBehavior.Never;
                context.BindingMetadata.IsBindingRequired = bindingBehavior.Behavior == BindingBehavior.Required;
            }

            var boundConstructor = FindBoundConstructor(context);
            if (boundConstructor != null)
            {
                context.BindingMetadata.BoundConstructor = boundConstructor;
            }
        }

        private ConstructorInfo FindBoundConstructor(BindingMetadataProviderContext context)
        {
            var type = context.Key.ModelType;

            if (type.IsAbstract || type.IsValueType || type.IsInterface)
            {
                return null;
            }

            var constructors = type.GetConstructors();
            if (constructors.Length == 0)
            {
                return null;
            }

            var modelBindingConstructors = constructors.Where(c => c.IsDefined(typeof(ModelBindingConstructor), inherit: true)).ToList();
            if (modelBindingConstructors.Count > 1)
            {
                throw new InvalidOperationException($"More than one constructor found on {type} with ModelBindingConstructorAttribute.");
            }
            else if (modelBindingConstructors.Count == 1)
            {
                return modelBindingConstructors[0];
            }

            var parameterlessConstructor = constructors.FirstOrDefault(c => c.GetParameters().Length == 0);
            if (parameterlessConstructor != null)
            {
                return parameterlessConstructor;
            }
            
            // Attempt to find the unequivocal longest constructor
            var orderedConstructors = constructors.OrderByDescending(c => c.GetParameters().Length).ToList();
            if (orderedConstructors.Count > 1 && orderedConstructors[1].GetParameters().Length == orderedConstructors[0].GetParameters().Length)
            {
                // Multiple constructors found with the same length. 
                return null;
            }

            return orderedConstructors[0];
        }

        private static BindingBehaviorAttribute FindBindingBehavior(BindingMetadataProviderContext context)
        {
            switch (context.Key.MetadataKind)
            {
                case ModelMetadataKind.Property:
                    // BindingBehavior can fall back to attributes on the Container Type, but we should ignore
                    // attributes on the Property Type.
                    var matchingAttributes = context.PropertyAttributes.OfType<BindingBehaviorAttribute>();
                    return matchingAttributes.FirstOrDefault()
                        ?? context.Key.ContainerType.GetTypeInfo()
                            .GetCustomAttributes(typeof(BindingBehaviorAttribute), inherit: true)
                            .OfType<BindingBehaviorAttribute>()
                            .FirstOrDefault();
                case ModelMetadataKind.Parameter:
                    return context.ParameterAttributes.OfType<BindingBehaviorAttribute>().FirstOrDefault();
                default:
                    return null;
            }
        }

        private class CompositePropertyFilterProvider : IPropertyFilterProvider
        {
            private readonly IEnumerable<IPropertyFilterProvider> _providers;

            public CompositePropertyFilterProvider(IEnumerable<IPropertyFilterProvider> providers)
            {
                _providers = providers;
            }

            public Func<ModelMetadata, bool> PropertyFilter => CreatePropertyFilter();

            private Func<ModelMetadata, bool> CreatePropertyFilter()
            {
                var propertyFilters = _providers
                    .Select(p => p.PropertyFilter)
                    .Where(p => p != null);

                return (m) =>
                {
                    foreach (var propertyFilter in propertyFilters)
                    {
                        if (!propertyFilter(m))
                        {
                            return false;
                        }
                    }

                    return true;
                };
            }
        }
    }
}
