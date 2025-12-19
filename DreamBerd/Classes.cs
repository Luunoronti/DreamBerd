using System;
using System.Collections.Generic;

namespace DreamberdInterpreter
{
    internal sealed class ClassDefinition
    {
        public string Name { get; }
        public Dictionary<string, Evaluator.FunctionDefinition> InstanceMethods { get; }
        public Dictionary<string, Evaluator.FunctionDefinition> StaticMethods { get; }
        public List<ClassPropertyDeclaration> Properties { get; }
        public Dictionary<string, Value> StaticFields { get; } = new(StringComparer.Ordinal);
        public HashSet<string> StaticPropertyNames { get; } = new(StringComparer.Ordinal);
        public string? InstanceFallback { get; set; }
        public string? StaticFallback { get; set; }

        public ClassDefinition(
            string name,
            Dictionary<string, Evaluator.FunctionDefinition> instanceMethods,
            Dictionary<string, Evaluator.FunctionDefinition> staticMethods,
            List<ClassPropertyDeclaration> properties)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            InstanceMethods = instanceMethods ?? throw new ArgumentNullException(nameof(instanceMethods));
            StaticMethods = staticMethods ?? throw new ArgumentNullException(nameof(staticMethods));
            Properties = properties ?? new List<ClassPropertyDeclaration>();

            foreach (var prop in Properties)
            {
                if (prop.IsStatic)
                    StaticPropertyNames.Add(prop.Name);
            }
        }
    }

    public sealed class ClassInstance
    {
        internal ClassInstance(ClassDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Fields = new Dictionary<string, Value>(StringComparer.Ordinal);
        }

        internal ClassDefinition Definition { get; }
        internal Dictionary<string, Value> Fields { get; }
        public bool Initialized { get; set; }
        public string Name => Definition.Name;
    }

    public sealed class BoundMethod
    {
        internal BoundMethod(ClassInstance target, string name, Evaluator.FunctionDefinition definition)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public ClassInstance Target { get; }
        public string Name { get; }
        internal Evaluator.FunctionDefinition Definition { get; }
    }

    internal sealed class FieldHistory
    {
        public List<Value> Values { get; } = new();
        public int Index;
    }
}
