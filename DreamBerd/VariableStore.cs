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
            public int DeclaredAtStatementIndex;
            public Value CurrentValue;
            public VariableHistory History = new();
            public LifetimeInfo? Lifetime;
        }

        /// <summary>
        /// W obrębie jednego scope'a możemy mieć wiele deklaracji tej samej zmiennej (overloading).
        /// Aktywna deklaracja wybierana jest wg:
        /// 1) najwyższy Priority (liczba '!'),
        /// 2) jeśli remis: najbardziej świeża deklaracja (DeclaredAtStatementIndex),
        /// 3) jeśli nadal remis: ostatnia na liście.
        /// </summary>
        private sealed class OverloadSet
        {
            public List<Entry> Entries = new();
        }

        private readonly List<Dictionary<string, OverloadSet>> _scopes =
            new()
            {
                new Dictionary<string, OverloadSet>(StringComparer.Ordinal)
            };

        private Dictionary<string, OverloadSet> CurrentScope => _scopes[_scopes.Count - 1];

        public void Clear()
        {
            _scopes.Clear();
            _scopes.Add(new Dictionary<string, OverloadSet>(StringComparer.Ordinal));
        }

        public void PushScope()
        {
            _scopes.Add(new Dictionary<string, OverloadSet>(StringComparer.Ordinal));
        }

        public void PopScope()
        {
            if (_scopes.Count <= 1)
                throw new InvalidOperationException("Cannot pop the global scope.");

            _scopes.RemoveAt(_scopes.Count - 1);
        }

        private bool TryFindSet(string name, out OverloadSet set, out Dictionary<string, OverloadSet> scope)
        {
            for (int i = _scopes.Count - 1; i >= 0; i--)
            {
                var sc = _scopes[i];
                if (sc.TryGetValue(name, out var found))
                {
                    set = found;
                    scope = sc;
                    return true;
                }
            }

            set = null!;
            scope = null!;
            return false;
        }

        private static Entry? SelectActive(OverloadSet set)
        {
            if (set.Entries.Count == 0)
                return null;

            Entry best = set.Entries[0];

            for (int i = 1; i < set.Entries.Count; i++)
            {
                var e = set.Entries[i];

                if (e.Priority > best.Priority)
                {
                    best = e;
                    continue;
                }

                if (e.Priority == best.Priority)
                {
                    if (e.DeclaredAtStatementIndex > best.DeclaredAtStatementIndex)
                    {
                        best = e;
                        continue;
                    }

                    if (e.DeclaredAtStatementIndex == best.DeclaredAtStatementIndex)
                    {
                        // w praktyce to się raczej nie zdarzy, ale dla pewności:
                        best = e;
                    }
                }
            }

            return best;
        }

        public void Declare(
            string name,
            Mutability mutability,
            Value initialValue,
            int priority,
            LifetimeSpecifier lifetime,
            int declarationIndex)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (priority <= 0) priority = 1; // sanity: pojedyncze '!' == 1

            // Deklaracje trafiają do bieżącego scope'a (blokowego).
            // Pozwalamy na shadowing nazw z parent scope'ów.
            var scope = CurrentScope;

            if (!scope.TryGetValue(name, out var set))
            {
                set = new OverloadSet();
                scope[name] = set;
            }

            var entry = new Entry
            {
                Mutability = mutability,
                Priority = priority,
                DeclaredAtStatementIndex = declarationIndex,
                CurrentValue = initialValue
            };

            entry.History.Values.Add(initialValue);
            entry.History.Index = 0;

            if (!lifetime.IsNone)
            {
                entry.Lifetime = new LifetimeInfo(lifetime, declarationIndex, DateTime.UtcNow);
            }

            set.Entries.Add(entry);
        }

        public bool TryGet(string name, out Value value)
        {
            value = default;

            if (!TryFindSet(name, out var set, out _))
                return false;

            var active = SelectActive(set);
            if (active == null)
                return false;

            value = active.CurrentValue;
            return true;
        }

        public void Assign(string name, Value newValue, int statementIndex)
        {
            if (!TryFindSet(name, out var set, out _))
                throw new InterpreterException($"Variable '{name}' is not defined.");

            var entry = SelectActive(set);
            if (entry == null)
                throw new InterpreterException($"Variable '{name}' is not defined.");

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
            if (TryFindSet(name, out _, out var scope))
            {
                scope.Remove(name);
            }
        }

        public void ExpireLifetimes(int currentStatementIndex, DateTime nowUtc)
        {
            // Usuwamy wygasłe overloady, a jeśli set jest pusty, usuwamy całą nazwę.
            foreach (var scope in _scopes)
            {
                // Musimy zbierać klucze do usunięcia, bo nie wolno modyfikować dict podczas foreach.
                List<string>? namesToRemove = null;

                foreach (var pair in scope)
                {
                    var set = pair.Value;
                    if (set.Entries.Count == 0)
                        continue;

                    // Czyścimy z tyłu (bezpiecznie usuwać elementy listy).
                    for (int i = set.Entries.Count - 1; i >= 0; i--)
                    {
                        var entry = set.Entries[i];
                        if (!entry.Lifetime.HasValue)
                            continue;

                        var info = entry.Lifetime.Value;
                        var lifetime = info.Lifetime;

                        bool expired = false;

                        switch (lifetime.Kind)
                        {
                            case LifetimeKind.None:
                                expired = false;
                                break;

                            case LifetimeKind.Infinity:
                                expired = false;
                                break;

                            case LifetimeKind.Lines:
                                {
                                    double age = currentStatementIndex - info.DeclarationIndex;
                                    if (age >= lifetime.Value)
                                        expired = true;
                                    break;
                                }

                            case LifetimeKind.Seconds:
                                {
                                    var age = nowUtc - info.CreatedAtUtc;
                                    if (age >= TimeSpan.FromSeconds(lifetime.Value))
                                        expired = true;
                                    break;
                                }

                            default:
                                throw new InterpreterException($"Unknown lifetime kind: {lifetime.Kind}");
                        }

                        if (expired)
                        {
                            set.Entries.RemoveAt(i);
                        }
                    }

                    if (set.Entries.Count == 0)
                    {
                        namesToRemove ??= new List<string>();
                        namesToRemove.Add(pair.Key);
                    }
                }

                if (namesToRemove != null)
                {
                    foreach (var name in namesToRemove)
                        scope.Remove(name);
                }
            }
        }

        public bool TryGetHistory(string name, out List<Value> values, out int currentIndex)
        {
            values = new List<Value>();
            currentIndex = 0;

            if (!TryFindSet(name, out var set, out _))
                return false;

            var entry = SelectActive(set);
            if (entry == null)
                return false;

            values = entry.History.Values;
            currentIndex = entry.History.Index;
            return true;
        }

        public bool TryPrevious(string name, out Value newValue, out bool changed)
        {
            newValue = default;
            changed = false;

            if (!TryFindSet(name, out var set, out _))
                return false;

            var entry = SelectActive(set);
            if (entry == null)
                return false;

            var hist = entry.History;
            if (hist.Values.Count == 0)
                return false;

            if (hist.Index <= 0)
            {
                newValue = hist.Values[0];
                changed = false;
                return true;
            }

            hist.Index--;
            newValue = hist.Values[hist.Index];

            if (!entry.CurrentValue.StrictEquals(newValue))
            {
                entry.CurrentValue = newValue;
                changed = true;
            }

            return true;
        }

        public bool TryNext(string name, out Value newValue, out bool changed)
        {
            newValue = default;
            changed = false;

            if (!TryFindSet(name, out var set, out _))
                return false;

            var entry = SelectActive(set);
            if (entry == null)
                return false;

            var hist = entry.History;
            if (hist.Values.Count == 0)
                return false;

            if (hist.Index >= hist.Values.Count - 1)
            {
                newValue = hist.Values[hist.Values.Count - 1];
                changed = false;
                return true;
            }

            hist.Index++;
            newValue = hist.Values[hist.Index];

            if (!entry.CurrentValue.StrictEquals(newValue))
            {
                entry.CurrentValue = newValue;
                changed = true;
            }

            return true;
        }

        private static void UpdateHistory(Entry entry, Value newValue)
        {
            var hist = entry.History;
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
        }
    }
}
