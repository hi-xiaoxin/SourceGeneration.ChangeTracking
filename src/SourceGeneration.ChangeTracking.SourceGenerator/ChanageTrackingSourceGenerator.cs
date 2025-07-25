﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace SourceGeneration.ChangeTracking.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public partial class ChanageTrackingSourceGenerator : IIncrementalGenerator
{
    public const string RootNamespace = "SourceGeneration.ChangeTracking";

    public const string ChangeTrackingAttribute = $"{RootNamespace}.ChangeTrackingAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var methodDeclarations = context.SyntaxProvider.ForAttributeWithMetadataName(
            ChangeTrackingAttribute,
            predicate: static (node, token) =>
            {
                if (node is not TypeDeclarationSyntax type || !type.IsPartial())
                {
                    return false;
                }

                if (!type.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ClassDeclaration) &&
                    !type.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.RecordDeclaration))
                {
                    return false;
                }

                //如果是内部类，需要确保父级都是 class，record 且为 partial
                var parent = type.Parent;
                while (parent != null)
                {
                    if (parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax)
                        break;

                    if (parent is not TypeDeclarationSyntax tp)
                        return false;

                    if (!tp.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ClassDeclaration) &&
                        !tp.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.RecordDeclaration))
                    {
                        return false;
                    }

                    if (!tp.IsPartial() || tp.TypeParameterList != null)
                        return false;

                    parent = parent.Parent;
                }

                return true;
            },
            transform: static (context, token) =>
            {
                return (TypeDeclarationSyntax)context.TargetNode;
            });

        var source = methodDeclarations.Combine(context.CompilationProvider);

        context.RegisterSourceOutput(source, static (sourceContext, source) =>
        {
            CancellationToken cancellationToken = sourceContext.CancellationToken;
            TypeDeclarationSyntax type = source.Left;
            Compilation compilation = source.Right;

            SemanticModel model = compilation.GetSemanticModel(type.SyntaxTree);
            var typeSymbol = (INamedTypeSymbol)model.GetDeclaredSymbol(type, cancellationToken)!;

            var typeProxy = CreateProxy(typeSymbol, cancellationToken);

            var root = (CompilationUnitSyntax)type.SyntaxTree.GetRoot();

            CSharpCodeBuilder builder = new();
            builder.AppendAutoGeneratedComment();

            if (typeProxy.Namespace == null)
            {
                foreach (var containerType in typeProxy.ContainerTypes)
                {
                    builder.AppendLine($"partial class {containerType}");
                    builder.AppendLine("{");
                    builder.IncreaseIndent();
                }

                CreateSource(typeProxy, builder);

                for (int i = 0; i < typeProxy.ContainerTypes.Count; i++)
                {
                    builder.DecreaseIndent();
                    builder.AppendLine("}");
                }
            }
            else
            {
                builder.AppendBlock($"namespace {typeProxy.Namespace}", () =>
                {
                    foreach (var containerType in typeProxy.ContainerTypes)
                    {
                        builder.AppendLine($"partial class {containerType}");
                        builder.AppendLine("{");
                        builder.IncreaseIndent();
                    }

                    CreateSource(typeProxy, builder);

                    for (int i = 0; i < typeProxy.ContainerTypes.Count; i++)
                    {
                        builder.DecreaseIndent();
                        builder.AppendLine("}");
                    }
                });
            }

            var code = builder.ToString();
            sourceContext.AddSource($"{typeProxy.Namespace ?? "gobal-"}.{typeProxy.MetadataName}.ChangeTacking.g.cs", code);
        });
    }

    private static void CreateSource(TypeDefinition typeProxy, CSharpCodeBuilder builder)
    {
        var typekind = typeProxy.IsRecord ? "record" : "class";

        string? interfaces = null;

        if (!typeProxy.BaseChangeTracking)
        {
            List<string> implementations = [];
            if (!typeProxy.NotifyPropertyChanging) implementations.Add("global::System.ComponentModel.INotifyPropertyChanging");
            if (!typeProxy.NotifyPropertyChanged) implementations.Add("global::System.ComponentModel.INotifyPropertyChanged");
            if (!typeProxy.NotifyCollectionChanged) implementations.Add("global::System.Collections.Specialized.INotifyCollectionChanged");
            if (!typeProxy.BaseChangeTracking) implementations.Add($"global::{RootNamespace}.ICascadingChangeTracking");

            if (implementations.Count > 0)
                interfaces = " : " + string.Join(", ", implementations);
        }

        builder.AppendBlock($"partial {typekind} {typeProxy.Name}{interfaces}", () =>
        {
            if (!typeProxy.BaseChangeTracking)
            {
                if (!typeProxy.NotifyPropertyChanging)
                    builder.AppendLine("public event global::System.ComponentModel.PropertyChangingEventHandler PropertyChanging;");

                if (!typeProxy.NotifyPropertyChanged)
                    builder.AppendLine("public event global::System.ComponentModel.PropertyChangedEventHandler PropertyChanged;");

                if (!typeProxy.NotifyCollectionChanged)
                    builder.AppendLine("public event global::System.Collections.Specialized.NotifyCollectionChangedEventHandler CollectionChanged;");

                builder.AppendLine("protected bool __cascadingChanged;");
                builder.AppendLine("protected bool __baseChanged;");
                builder.AppendLine();

                builder.AppendLine("bool global::System.ComponentModel.IChangeTracking.IsChanged => __baseChanged || __cascadingChanged;");
                builder.AppendLine($"bool global::{RootNamespace}.ICascadingChangeTracking.IsCascadingChanged => __cascadingChanged;");
                builder.AppendLine($"bool global::{RootNamespace}.ICascadingChangeTracking.IsBaseChanged => __baseChanged;");

                builder.AppendLine();
                builder.AppendBlock("protected void OnPropertyChanging(string propertyName)", () =>
                {
                    builder.AppendLine("PropertyChanging?.Invoke(this, new global::System.ComponentModel.PropertyChangingEventArgs(propertyName));");
                });

                builder.AppendLine();
                builder.AppendBlock("protected void OnPropertyChanging(object sender, global::System.ComponentModel.PropertyChangingEventArgs e)", () =>
                {
                    builder.AppendLine("PropertyChanging?.Invoke(sender, e);");
                });

                builder.AppendLine();
                builder.AppendBlock("protected void OnPropertyChanged(string propertyName)", () =>
                {
                    builder.AppendLine("__baseChanged = true;");
                    builder.AppendLine("PropertyChanged?.Invoke(this, new global::System.ComponentModel.PropertyChangedEventArgs(propertyName));");
                });

                builder.AppendLine();
                builder.AppendBlock("protected void OnPropertyChanged(object sender, global::System.ComponentModel.PropertyChangedEventArgs e)", () =>
                {
                    builder.AppendLine("__cascadingChanged = true;");
                    builder.AppendLine("PropertyChanged?.Invoke(sender, e);");
                });

                builder.AppendLine();
                builder.AppendBlock("protected void OnCollectionChanged(object sender, global::System.Collections.Specialized.NotifyCollectionChangedEventArgs e)", () =>
                {
                    builder.AppendLine("__cascadingChanged = true;");
                    builder.AppendLine("CollectionChanged?.Invoke(sender, e);");
                });

                builder.AppendLine();
            }

            if (typeProxy.BaseChangeTracking)
            {
                builder.AppendBlock($"protected override void __AcceptChanges()", () =>
                {
                    builder.AppendBlock("if (__cascadingChanged)", () =>
                    {
                        var properites = typeProxy.Properties.Where(x => x.ChangeTracking).ToList();
                        if (properites.Count > 0)
                        {
                            foreach (var property in properites)
                            {
                                builder.AppendLine($"((global::System.ComponentModel.IChangeTracking)this.{property.PropertyName})?.AcceptChanges();");
                            }
                        }
                    });

                    builder.AppendLine("base.__AcceptChanges();");
                });
            }
            else
            {
                var virtual_flags = typeProxy.IsSealed ? string.Empty : "virtual ";
                builder.AppendBlock($"protected {virtual_flags}void __AcceptChanges()", () =>
                {
                    builder.AppendBlock("if (__cascadingChanged)", () =>
                    {
                        var properites = typeProxy.Properties.Where(x => x.ChangeTracking).ToList();
                        if (properites.Count > 0)
                        {
                            foreach (var property in properites)
                            {
                                builder.AppendLine($"((global::System.ComponentModel.IChangeTracking)this.{property.PropertyName})?.AcceptChanges();");
                            }
                        }
                        builder.AppendLine("__cascadingChanged = false;");
                    });
                    builder.AppendLine("__baseChanged = false;");
                });

                builder.AppendLine();

                builder.AppendBlock($"void global::System.ComponentModel.IChangeTracking.AcceptChanges()", () =>
                {
                    builder.AppendLine("__AcceptChanges();");
                });
            }
            builder.AppendLine();

            foreach (var property in typeProxy.Properties.Where(x => x.HasInitializer))
            {
                builder.AppendLine($"private bool ___s_{property.PropertyName}___;");
            }
            builder.AppendLine();

            foreach (var property in typeProxy.Properties)
            {
                StringBuilder modifers = new();

                modifers.Append(GetPropertyAccessibilityString(property.Accessibility));

                if (property.IsSealed) modifers.Append("sealed ");

                if (property.IsVirtual)
                    modifers.Append("virtual ");
                else if (property.IsOverride)
                    modifers.Append("override ");

                if (property.IsRequired)
                    modifers.Append("required ");

                //builder.AppendLine($"private {property.Type} {property.FieldName};");

                builder.AppendBlock($"{modifers}partial {property.Type} {property.PropertyName}", () =>
                {
                    if (property.GetAccessibility.HasValue)
                    {
                        string? getAccessibility = null;
                        if (property.GetAccessibility.Value != property.Accessibility)
                        {
                            getAccessibility = GetPropertyAccessibilityString(property.GetAccessibility.Value);
                        }

                        builder.AppendBlock($"{getAccessibility}get", () =>
                        {
                            EmitGetMethod(builder, property);
                        });

                    }
                    if (property.SetAccessibility.HasValue)
                    {
                        string? setAccessibility = null;
                        if (property.SetAccessibility.Value != property.Accessibility)
                        {
                            setAccessibility = GetPropertyAccessibilityString(property.SetAccessibility.Value);
                        }

                        builder.AppendBlock(property.IsInitOnly ? $"{setAccessibility}init" : $"{setAccessibility}set", () =>
                        {
                            EmitSetMethod(builder, property);
                        });
                    }
                });
                builder.AppendLine();
            }
        });
    }

    private static string GetPropertyAccessibilityString(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public ",
            Accessibility.Protected => "protected ",
            Accessibility.Internal => "internal ",
            Accessibility.ProtectedOrInternal => "protected internal ",
            Accessibility.ProtectedAndInternal => "protected internal ",
            _ => "private "
        };
    }

    private static void EmitGetMethod(CSharpCodeBuilder builder, PropertyDefinition property)
    {
        if (property.HasInitializer)
        {
            builder.AppendBlock($"if (!___s_{property.PropertyName}___ && field is not null)", () =>
            {
                builder.AppendLine($"___s_{property.PropertyName}___ = true;");

                if (property.NotifyPropertyChanging)
                {
                    builder.AppendLine($"((global::System.ComponentModel.INotifyPropertyChanging)field).PropertyChanging += OnPropertyChanging;");
                }
                if (property.NotifyPropertyChanged)
                {
                    builder.AppendLine($"((global::System.ComponentModel.INotifyPropertyChanged)field).PropertyChanged += OnPropertyChanged;");
                }
                if (property.NotifyCollectionChanged)
                {
                    builder.AppendLine($"((global::System.Collections.Specialized.INotifyCollectionChanged)field).CollectionChanged += OnCollectionChanged;");
                }
                if (property.ChangeTracking)
                {
                    builder.AppendLine($"__cascadingChanged |= ((global::System.ComponentModel.IChangeTracking)field).IsChanged;");
                }

            });
        }

        builder.AppendLine($"return field;");
    }

    private static void EmitSetMethod(CSharpCodeBuilder builder, PropertyDefinition property)
    {
        if (property.HasInitializer)
        {
            builder.AppendLine($"___s_{property.PropertyName}___ = true;");
        }

        builder.AppendBlock($"if (!global::System.Collections.Generic.EqualityComparer<{property.Type}>.Default.Equals(field, value))", () =>
        {
            builder.AppendLine($"OnPropertyChanging(\"{property.PropertyName}\");");

            if (property.Kind == TypeProxyKind.Value)
            {
                builder.AppendLine($"field = value;");
            }
            else
            {
                if (property.NotifyPropertyChanging || property.NotifyPropertyChanged || property.NotifyCollectionChanged)
                {
                    builder.AppendBlock($"if (field is not null)", () =>
                    {
                        if (property.NotifyPropertyChanging)
                        {
                            builder.AppendLine($"((global::System.ComponentModel.INotifyPropertyChanging)field).PropertyChanging -= OnPropertyChanging;");
                            //builder.AppendBlock($"if ({property.FieldName} is global::System.ComponentModel.INotifyPropertyChanging __propertyChanging__)", () =>
                            //{
                            //    builder.AppendLine("__propertyChanging__.PropertyChanging -= OnPropertyChanging;");
                            //});
                        }
                        if (property.NotifyPropertyChanged)
                        {
                            builder.AppendLine($"((global::System.ComponentModel.INotifyPropertyChanged)field).PropertyChanged -= OnPropertyChanged;");
                            //builder.AppendBlock($"if ({property.FieldName} is global::System.ComponentModel.INotifyPropertyChanged __propertyChanged__)", () =>
                            //{
                            //    builder.AppendLine("__propertyChanged__.PropertyChanged -= OnPropertyChanged;");
                            //});
                        }
                        if (property.NotifyCollectionChanged)
                        {
                            builder.AppendLine($"((global::System.Collections.Specialized.INotifyCollectionChanged)field).CollectionChanged -= OnCollectionChanged;");
                            //builder.AppendBlock($"if ({property.FieldName} is global::System.Collections.Specialized.INotifyCollectionChanged __collectionChanged__)", () =>
                            //{
                            //    builder.AppendLine("__collectionChanged__.CollectionChanged -= OnCollectionChanged;");
                            //});
                        }
                    });
                    builder.AppendLine();
                }

                builder.AppendBlock("if (value is null)", () =>
                {
                    builder.AppendLine($"field = null;");
                });
                builder.AppendBlock("else", () =>
                {
                    SetPropertyProxy(builder, property);
                });
            }

            builder.AppendLine($"OnPropertyChanged(\"{property.PropertyName}\");");
        });
        if (property.Kind == TypeProxyKind.Collection)
        {
            builder.AppendBlock("else if (value is not null && value is not global::System.ComponentModel.IChangeTracking)", () =>
            {
                SetPropertyProxy(builder, property);
            });
        }
    }

    private static void SetPropertyProxy(CSharpCodeBuilder builder, PropertyDefinition property)
    {
        if (property.Kind == TypeProxyKind.Collection)
        {
            builder.AppendLine($"field = new global::{RootNamespace}.ChangeTrackingList<{property.ElementType}>(value);");
        }
        else if (property.Kind == TypeProxyKind.Dictionary)
        {
            builder.AppendLine($"field = new global::{RootNamespace}.ChangeTrackingDictionary<{property.KeyType}, {property.ElementType}>(value);");
        }
        else
        {
            builder.AppendLine($"field = value;");
        }

        if (property.NotifyPropertyChanging)
        {
            builder.AppendLine($"((global::System.ComponentModel.INotifyPropertyChanging)field).PropertyChanging += OnPropertyChanging;");
        }
        if (property.NotifyPropertyChanged)
        {
            builder.AppendLine($"((global::System.ComponentModel.INotifyPropertyChanged)field).PropertyChanged += OnPropertyChanged;");
        }
        if (property.NotifyCollectionChanged)
        {
            builder.AppendLine($"((global::System.Collections.Specialized.INotifyCollectionChanged)field).CollectionChanged += OnCollectionChanged;");
        }
        if (property.ChangeTracking)
        {
            builder.AppendLine($"__cascadingChanged |= ((global::System.ComponentModel.IChangeTracking)field).IsChanged;");
        }
    }

    private static TypeDefinition CreateProxy(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        List<string> containers = [];

        INamedTypeSymbol? container = type.ContainingType;
        while (container != null)
        {
            containers.Insert(0, container.Name);
            container = container.ContainingType;
        }

        var properties = type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(x => !x.IsStatic && x.IsPartialDefinition);

        string name;
        if (type.TypeParameters != null && type.TypeParameters.Length > 0)
        {
            name = $"{type.Name}<{string.Join(", ", type.TypeParameters.Select(x => x.Name))}>";
        }
        else
        {
            name = type.Name;
        }
        
        TypeDefinition typeProxy = new(name, type.MetadataName, type.GetNamespace(), type.IsRecord, type.IsSealed);

        typeProxy.ContainerTypes.AddRange(containers);

        foreach (var property in properties)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var propertyDefinition = CreateProperty(property);
            if (propertyDefinition != null)
            {
                if ((propertyDefinition.NotifyCollectionChanged ||
                    propertyDefinition.NotifyPropertyChanging ||
                    propertyDefinition.NotifyPropertyChanged ||
                    propertyDefinition.ChangeTracking) &&
                    property.DeclaringSyntaxReferences.Length > 0)
                {
                    var node = property.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken);
                    if (node is PropertyDeclarationSyntax propertySyntax)
                    {
                        propertyDefinition.HasInitializer = propertySyntax.Initializer != null;
                    }
                    //propertyDefinition.HasDeclaring = true;
                }

                typeProxy.Properties.Add(propertyDefinition);
            }
        }

        if (type.BaseType?.HasAttribute(ChangeTrackingAttribute) == true)
        {
            typeProxy.NotifyPropertyChanging = true;
            typeProxy.NotifyPropertyChanged = true;
            typeProxy.NotifyPropertyChanged = true;
            typeProxy.BaseChangeTracking = true;
        }
        else
        {
            CheckTypeInterface(type, out bool notifyPropertyChanging, out bool notifyPropertyChanged, out bool notifyCollectionChanged);
            typeProxy.NotifyPropertyChanging = notifyPropertyChanging;
            typeProxy.NotifyPropertyChanged = notifyPropertyChanged;
            typeProxy.NotifyCollectionChanged = notifyCollectionChanged;
            typeProxy.BaseChangeTracking = false;
        }

        return typeProxy;


        static PropertyDefinition? CreateProperty(IPropertySymbol property)
        {
            var type = property.Type;
            var typeName = property.Type.GetFullName();
            var propertyName = property.Name;

            if (type.IsValueType ||
                type.TypeKind == TypeKind.Struct ||
                type.TypeKind == TypeKind.Enum ||
                type.IsTupleType ||
                typeName == "string")
            {
                return new PropertyDefinition(TypeProxyKind.Value, propertyName, typeName)
                {
                    Accessibility = property.DeclaredAccessibility,
                    GetAccessibility = property.GetMethod?.DeclaredAccessibility,
                    SetAccessibility = property.SetMethod?.DeclaredAccessibility,

                    IsInitOnly = property.SetMethod?.IsInitOnly == true,
                    IsRequired = property.IsRequired,
                    IsOverride = property.IsOverride,
                    IsSealed = property.IsSealed,
                    IsVirtual = property.IsVirtual,
                };
            }

            if (type is INamedTypeSymbol namedType && namedType.IsGenericType)
            {
                var definition = type.OriginalDefinition.GetFullName();

                if (definition == "global::System.Collections.Generic.IDictionary<TKey, TValue>")
                {
                    return new PropertyDefinition(TypeProxyKind.Dictionary, propertyName, typeName)
                    {
                        Accessibility = property.DeclaredAccessibility,
                        GetAccessibility = property.GetMethod?.DeclaredAccessibility,
                        SetAccessibility = property.SetMethod?.DeclaredAccessibility,

                        IsInitOnly = property.SetMethod?.IsInitOnly == true,
                        IsRequired = property.IsRequired,
                        IsOverride = property.IsOverride,
                        IsSealed = property.IsSealed,
                        IsVirtual = property.IsVirtual,
                        KeyType = namedType.TypeArguments[0].GetFullName(),
                        ElementType = namedType.TypeArguments[1].GetFullName(),
                        NotifyCollectionChanged = true,
                        NotifyPropertyChanged = true,
                        ChangeTracking = true,
                    };
                }
                else if (definition == "global::System.Collections.Generic.IList<T>" ||
                         definition == "global::System.Collections.Generic.ICollection<T>" ||
                         definition == "global::System.Collections.Generic.IEnumerable<T>")
                {
                    return new PropertyDefinition(TypeProxyKind.Collection, propertyName, typeName)
                    {
                        Accessibility = property.DeclaredAccessibility,
                        GetAccessibility = property.GetMethod?.DeclaredAccessibility,
                        SetAccessibility = property.SetMethod?.DeclaredAccessibility,

                        IsInitOnly = property.SetMethod?.IsInitOnly == true,
                        IsRequired = property.IsRequired,
                        IsOverride = property.IsOverride,
                        IsSealed = property.IsSealed,
                        IsVirtual = property.IsVirtual,
                        ElementType = namedType.TypeArguments[0].GetFullName(),
                        NotifyCollectionChanged = true,
                        NotifyPropertyChanged = true,
                        ChangeTracking = true,
                    };
                }
            }

            if (type.HasAttribute(ChangeTrackingAttribute))
            {
                return new PropertyDefinition(TypeProxyKind.Object, propertyName, typeName)
                {
                    Accessibility = property.DeclaredAccessibility,
                    GetAccessibility = property.GetMethod?.DeclaredAccessibility,
                    SetAccessibility = property.SetMethod?.DeclaredAccessibility,

                    IsInitOnly = property.SetMethod?.IsInitOnly == true,
                    IsRequired = property.IsRequired,
                    IsOverride = property.IsOverride,
                    IsSealed = property.IsSealed,
                    IsVirtual = property.IsVirtual,
                    NotifyCollectionChanged = true,
                    NotifyPropertyChanging = true,
                    NotifyPropertyChanged = true,
                    ChangeTracking = true,
                };
            }
            else
            {
                CheckPropertyInterface(type, out bool notifyPropertyChanging, out bool notifyPropertyChanged, out bool notifyCollectionChanged, out bool changeTracking);
                return new PropertyDefinition(TypeProxyKind.Object, propertyName, typeName)
                {
                    Accessibility = property.DeclaredAccessibility,
                    GetAccessibility = property.GetMethod?.DeclaredAccessibility,
                    SetAccessibility = property.SetMethod?.DeclaredAccessibility,

                    IsInitOnly = property.SetMethod?.IsInitOnly == true,
                    IsRequired = property.IsRequired,
                    IsOverride = property.IsOverride,
                    IsSealed = property.IsSealed,
                    IsVirtual = property.IsVirtual,

                    NotifyCollectionChanged = notifyCollectionChanged,
                    NotifyPropertyChanging = notifyPropertyChanging,
                    NotifyPropertyChanged = notifyPropertyChanged,
                    ChangeTracking = changeTracking,
                };
            }
        }

        static void CheckTypeInterface(ITypeSymbol type, out bool notifyPropertyChanging, out bool notifyPropertyChanged, out bool notifyCollectionChanged)
        {
            notifyPropertyChanging = false;
            notifyPropertyChanged = false;
            notifyCollectionChanged = false;
            foreach (var @interface in type.AllInterfaces)
            {
                var fullName = @interface.GetFullName();

                if (fullName == "global::System.ComponentModel.INotifyPropertyChanging")
                {
                    notifyPropertyChanging = true;
                }
                else if (fullName == "global::System.ComponentModel.INotifyPropertyChanged")
                {
                    notifyPropertyChanged = true;
                }
                else if (fullName == "global::System.Collections.Specialized.INotifyCollectionChanged")
                {
                    notifyCollectionChanged = true;
                }

                if (notifyPropertyChanging && notifyPropertyChanged && notifyCollectionChanged)
                    break;
            }
        }

        static void CheckPropertyInterface(ITypeSymbol type, out bool notifyPropertyChanging, out bool notifyPropertyChanged, out bool notifyCollectionChanged, out bool changeTracking)
        {
            notifyPropertyChanging = false;
            notifyPropertyChanged = false;
            notifyCollectionChanged = false;
            changeTracking = false;

            foreach (var @interface in type.AllInterfaces)
            {
                var fullName = @interface.GetFullName();

                if (fullName == "global::System.ComponentModel.INotifyPropertyChanging")
                {
                    notifyPropertyChanging = true;
                }
                else if (fullName == "global::System.ComponentModel.INotifyPropertyChanged")
                {
                    notifyPropertyChanged = true;
                }
                else if (fullName == "global::System.Collections.Specialized.INotifyCollectionChanged")
                {
                    notifyCollectionChanged = true;
                }
                else if (fullName == "global::System.ComponentModel.IChangeTracking")
                {
                    changeTracking = true;
                }

                if (notifyPropertyChanging && notifyPropertyChanged && notifyCollectionChanged && changeTracking)
                    break;
            }
        }
    }

    private sealed class TypeDefinition(string name, string metadataName, string? ns, bool isRecord, bool isSealed)
    {
        public readonly string? Namespace = ns;
        public readonly string Name = name;
        public readonly string MetadataName = metadataName;
        public readonly bool IsRecord = isRecord;
        public readonly bool IsSealed = isSealed;

        public bool NotifyPropertyChanged;
        public bool NotifyPropertyChanging;
        public bool NotifyCollectionChanged;
        public bool BaseChangeTracking;

        public readonly List<string> ContainerTypes = [];
        public readonly List<PropertyDefinition> Properties = [];
    }

    private sealed class PropertyDefinition(TypeProxyKind proxyKind, string propertyName, string type)
    {
        //public readonly string FieldName = "__" + char.ToLower(PropertyName[0]) + propertyName.Substring(1);
        public readonly string PropertyName = propertyName;
        public readonly string Type = type;
        public readonly TypeProxyKind Kind = proxyKind;


        public Accessibility Accessibility;
        public Accessibility? GetAccessibility;
        public Accessibility? SetAccessibility;

        public bool IsRequired;
        public bool IsInitOnly;
        public bool IsVirtual;
        public bool IsSealed;
        public bool IsOverride;

        public bool HasInitializer;

        public string? KeyType;
        public string? ElementType;

        public bool NotifyPropertyChanged;
        public bool NotifyPropertyChanging;
        public bool NotifyCollectionChanged;
        public bool ChangeTracking;

    }

    private enum TypeProxyKind
    {
        Value,
        Object,
        Collection,
        Dictionary,
    }
}

