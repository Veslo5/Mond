﻿using Microsoft.CodeAnalysis;

namespace Mond.SourceGenerator;

public partial class MondSourceGenerator
{
    private static void ModuleBindings(GeneratorExecutionContext context, INamedTypeSymbol module, IndentTextWriter writer)
    {
        var moduleName = module.GetAttributes().TryGetAttribute("MondModuleAttribute", out var moduleAttr)
            ? moduleAttr.GetArgument() ?? module.Name
            : module.Name;

        var properties = GetProperties(context, module, true);
        var methods = GetMethods(context, module, true);
        var methodTables = MethodTable.Build(methods);

        writer.WriteLine("public sealed class Library : IMondLibrary");
        writer.OpenBracket();

        writer.WriteLine("IEnumerable<KeyValuePair<string, MondValue>> IMondLibrary.GetDefinitions(MondState state)");
        writer.OpenBracket();

        foreach (var (property, name) in properties)
        {
            if (property.GetMethod is { DeclaredAccessibility: Accessibility.Public })
            {
                writer.WriteLine($"yield return new KeyValuePair<string, MondValue>(\"get{name}\", MondValue.Function({name}__Getter));");
            }

            if (property.SetMethod is { DeclaredAccessibility: Accessibility.Public })
            {
                writer.WriteLine($"yield return new KeyValuePair<string, MondValue>(\"set{name}\", MondValue.Function({name}__Setter));");
            }
        }

        foreach (var table in methodTables)
        {
            writer.WriteLine($"yield return new KeyValuePair<string, MondValue>(\"{table.Name}\", MondValue.Function({table.Name}__Dispatch));");
        }

        if (methods.Count == 0)
        {
            writer.WriteLine("yield break;");
        }

        writer.CloseBracket();
        writer.WriteLine();

        var qualifier = $"global::{module.GetFullNamespace()}.{module.Name}";

        foreach (var (property, name) in properties)
        {
            if (property.GetMethod is { DeclaredAccessibility: Accessibility.Public })
            {
                writer.WriteLine($"private static MondValue {name}__Getter(MondState state, params MondValue[] args)");
                writer.OpenBracket();
                
                writer.WriteLine("if (args.Length != 0)");
                writer.OpenBracket();
                writer.WriteLine($"throw new MondRuntimeException(\"{moduleName}.get{name}: expected 0 arguments\");");
                writer.CloseBracket();

                writer.WriteLine($"var value = {qualifier}.{property.Name};");
                writer.WriteLine($"return {ConvertToMondValue("value", property.Type)};");
                writer.CloseBracket();
                writer.WriteLine();
            }

            if (property.SetMethod is { DeclaredAccessibility: Accessibility.Public })
            {
                var parameter = new Parameter(property.SetMethod.Parameters[0]);

                writer.WriteLine($"private static MondValue {name}__Setter(MondState state, params MondValue[] args)");
                writer.OpenBracket();

                writer.WriteLine($"if (args.Length != 1 || !{CompareArgument(0, parameter.MondTypes)})");
                writer.OpenBracket();
                writer.WriteLine($"throw new MondRuntimeException(\"{moduleName}.set{name}: expected 1 argument of type {parameter.TypeName}\");");
                writer.CloseBracket();

                writer.WriteLine($"{qualifier}.{property.Name} = {ConvertFromMondValue("args[0]", property.Type)};");

                writer.WriteLine("return default;");
                writer.CloseBracket();
                writer.WriteLine();
            }
        }

        foreach (var table in methodTables)
        {
            writer.WriteLine($"private static MondValue {table.Name}__Dispatch(MondState state, params MondValue[] args)");
            writer.OpenBracket();

            writer.WriteLine("switch (args.Length)");
            writer.OpenBracket();

            for (var i = 0; i < table.Methods.Count; i++)
            {
                var tableMethods = table.Methods[i];
                if (tableMethods.Count == 0)
                {
                    continue;
                }

                writer.WriteLine($"case {i}:");
                writer.OpenBracket();
                foreach (var method in tableMethods)
                {
                    writer.WriteLine($"if ({CompareArguments(method)})");
                    writer.OpenBracket();
                    CallMethod(writer, qualifier, method);
                    writer.CloseBracket();
                }
                writer.WriteLine("break;");
                writer.CloseBracket();
            }

            writer.CloseBracket();

            foreach (var method in table.ParamsMethods)
            {
                writer.WriteLine($"if (args.Length >= {method.RequiredMondParameterCount} && {CompareArguments(method)})");
                writer.OpenBracket();
                CallMethod(writer, qualifier, method);
                writer.CloseBracket();
            }

            writer.WriteLine("return default;"); // todo: throw exception - no method matched

            writer.CloseBracket();
            writer.WriteLine();
        }

        writer.CloseBracket();
    }
}
