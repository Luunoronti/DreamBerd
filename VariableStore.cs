using System;
using System.Collections.Generic;

namespace DreamberdInterpreter
{
    public sealed class VariableStore
    {
        private sealed class VariableHistory
        {
            public List<Value> Values = new();
            public int Index;
        }

        private readonly struct LifetimeInfo
        {
            public LifetimeSpecifier Lifetime
            {
                get;
            }
            public int DeclarationIndex
            {
                get;
            }
            public DateTime CreatedAtUtc
            {
                get;
            }

            public LifetimeInfo(LifetimeSpecifier lifetime, int declarationIndex, DateTime createdAtUtc)
            {
                Lifetime = lifetime;
                DeclarationIndex = declarationIndex;
                CreatedAtUtc = createdAtUtc;
            }
        }

        private sealed class Entry
        {
            public Mutability Mutability = Mutability.VarVar;
            public int Priority;
            public Value CurrentValue;
            public VariableHistory History = new();
            public LifetimeInfo? Lifetime;
        }

        private readonly List<Dictionary<string, Entry>> _scopes =
            new()
            {
                new Dictionary<string, Entry>(StringComparer.Ordinal)
            };

        private Dictionary<string, Entry> CurrentScope => _scopes[_scopes.Count - 1];

        public void PushScope()
        {
            _scopes.Add(new Dictionary<string, Entry>(StringComparer.Ordinal));
        }

        public void PopScope()
        {
            if (_scopes.Count <= 1)
                throw new InvalidOperationException("Cannot pop the global scope.");

            _scopes.RemoveAt(_scopes.Count - 1);
        }

        private bool TryFindEntry(string name, out Entry? entry, out Dictionary<string, Entry>? scope)
        {
            for (int i = _scopes.Count - 1; i >= 0; i--)
            {
                var dict = _scopes[i];
                if (dict.TryGetValue(name, out var found))
                {
                    entry = found;
                    scope = dict;
                    return true;
                }
            }

            entry = null;
            scope = null;
            return false;
        }

        private const int MaxHistory = 100;
public void Declare(
            string name,
            Mutability mutability,
            Value initialValue,
            int priority,
            LifetimeSpecifier lifetime,
            int declarationIndex)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));

            // Deklaracje trafiają do bieżącego scope'a (blokowego).
            // Pozwalamy na "shadowing" nazw z parent scope'ów.
            var scope = CurrentScope;

            if (scope.TryGetValue(name, out var existing))
            {
                // Priorytet działa tylko w obrębie tego samego scope'a.
                if (priority < existing.Priority)
                    return;
            }

            var entry = new Entry
            {
                Mutability = mutability,
                Priority = priority,
                CurrentValue = initialValue,
                History = new VariableHistory()
            };

            entry.History.Values.Add(initialValue);
            entry.History.Index = 0;

            if (!lifetime.IsNone)
            {
                entry.Lifetime = new LifetimeInfo(lifetime, declarationIndex, DateTime.UtcNow);
            }

            scope[name] = entry;
        }


	public bool TryGet(string name, out Value value)
        {
            if (TryFindEntry(name, out var entry, out _))
            {
                value = entry!.CurrentValue;
                return true;
            }

            value = default;
            return false;
        }


public Value Get(string name)
        {
            if (TryFindEntry(name, out var entry, out _))
                return entry!.CurrentValue;

            throw new InterpreterException($"Variable '{name}' is not defined.");
        }


public void Assign(string name, Value newValue, int statementIndex)
        {
            if (!TryFindEntry(name, out var entry, out _) || entry == null)
                throw new InterpreterException($"Variable '{name}' is not defined.");

            //entry = entry!;

            if (entry.Mutability == Mutability.ConstConst ||
                entry.Mutability == Mutability.ConstVar)
            {
                throw new InterpreterException($"Variable '{name}' is not assignable.");
            }

            entry.CurrentValue = newValue;
            UpdateHistory(entry, newValue);
        }


public void Delete(string name)
        {
            if (TryFindEntry(name, out _, out var scope))
            {
                scope!.Remove(name);
            }
        }


public void ExpireLifetimes(int currentStatementIndex, DateTime nowUtc)
        {
            var toRemove = new List<(Dictionary<string, Entry> Scope, string Name)>();

            // Sprawdzamy wszystkie scope'y (od globalnego po najgłębszy).
            foreach (var scope in _scopes)
            {
                foreach (var pair in scope)
                {
                    string name = pair.Key;
                    var entry = pair.Value;

                    if (!entry.Lifetime.HasValue)
                        continue;

                    var info = entry.Lifetime.Value;
                    var lifetime = info.Lifetime;

                    switch (lifetime.Kind)
                    {
                        case LifetimeKind.None:
                            break;

                        case LifetimeKind.Infinity:
                            break;

                        // <N> (linie / statementy)
                        case LifetimeKind.Lines:
                            {
                                int age = currentStatementIndex - info.DeclarationIndex;
                                if (age >= lifetime.Value)
                                    toRemove.Add((scope, name));
                                break;
                            }

                        // <Ns> (czas)
                        case LifetimeKind.Seconds:
                            {
                                var age = nowUtc - info.CreatedAtUtc;
                                if (age.TotalSeconds >= lifetime.Value)
                                    toRemove.Add((scope, name));
                                break;
                            }

                        default:
                            throw new InterpreterException($"Unknown lifetime kind: {lifetime.Kind}");
                    }
                }
            }

            foreach (var (scope, name) in toRemove)
            {
                scope.Remove(name);
            }
        }


public bool TryGetHistory(string name, out IReadOnlyList<Value> values, out int currentIndex)
        {
            if (TryFindEntry(name, out var entry, out _))
            {
                values = entry!.History.Values;
                currentIndex = entry!.History.Index;
                return true;
            }

            values = Array.Empty<Value>();
            currentIndex = -1;
            return false;
        }


public bool TryPrevious(string name, out Value newValue, out bool changed)
        {
            newValue = default;
            changed = false;

            if (!TryFindEntry(name, out var entry, out _) || entry == null)
                return false;

            //entry = entry!;

            var hist = entry.History;
            if (hist.Values.Count == 0)
                return false;

            if (hist.Index <= 0)
            {
                newValue = hist.Values[0];
                changed = false;
                entry.CurrentValue = newValue;
                return true;
            }

            hist.Index--;
            newValue = hist.Values[hist.Index];
            changed = true;
            entry.CurrentValue = newValue;
            return true;
        }


public bool TryNext(string name, out Value newValue, out bool changed)
        {
            newValue = default;
            changed = false;

            if (!TryFindEntry(name, out var entry, out _) || entry == null)
                return false;

            //entry = entry!;

            var hist = entry.History;
            if (hist.Values.Count == 0)
                return false;

            if (hist.Index >= hist.Values.Count - 1)
            {
                newValue = hist.Values[hist.Values.Count - 1];
                changed = false;
                entry.CurrentValue = newValue;
                return true;
            }

            hist.Index++;
            newValue = hist.Values[hist.Index];
            changed = true;
            entry.CurrentValue = newValue;
            return true;
        }


private void UpdateHistory(Entry entry, Value newValue)
        {
            var hist = entry.History ??= new VariableHistory();

            if (hist.Values.Count == 0)
            {
                hist.Values.Add(newValue);
                hist.Index = 0;
                return;
            }

            var current = hist.Values[hist.Index];

            if (current.StrictEquals(newValue))
                return;

            if (hist.Index < hist.Values.Count - 1)
            {
                hist.Values.RemoveRange(hist.Index + 1, hist.Values.Count - hist.Index - 1);
            }

            hist.Values.Add(newValue);
            hist.Index = hist.Values.Count - 1;

            if (hist.Values.Count > MaxHistory)
            {
                hist.Values.RemoveAt(0);
                hist.Index--;
                if (hist.Index < 0) hist.Index = 0;
            }
        }
    }
}
