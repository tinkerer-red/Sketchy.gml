namespace Sketchy
{
	internal sealed class MacroDefinition
	{
		public string Name { get; }
		public string[] Parameters { get; }
		public string BodySingleLine { get; }
		public string FilePath { get; }
		public int LineStart { get; }
		public int LineEnd { get; }

		public bool HasParameters => Parameters.Length > 0;

		public MacroDefinition(string _name, string[] _parameters, string _body_single_line, string _file_path, int _line_start, int _line_end)
		{
			Name = _name;
			Parameters = _parameters;
			BodySingleLine = _body_single_line;
			FilePath = _file_path;
			LineStart = _line_start;
			LineEnd = _line_end;
		}
	}

	internal sealed class MacroTable
	{
		private readonly Dictionary<string, MacroDefinition> _macros_by_name = new Dictionary<string, MacroDefinition>(StringComparer.Ordinal);

		public IReadOnlyDictionary<string, MacroDefinition> MacrosByName => _macros_by_name;

		public bool TryGet(string _name, out MacroDefinition _macro)
		{
			return _macros_by_name.TryGetValue(_name, out _macro!);
		}

		public void AddOrThrow(MacroDefinition _macro)
		{
			if (_macros_by_name.TryGetValue(_macro.Name, out MacroDefinition? _existing))
			{
				throw new InvalidOperationException($"Macro redefinition '{_macro.Name}'\n  First: {_existing.FilePath}:{_existing.LineStart + 1}\n  Again: {_macro.FilePath}:{_macro.LineStart + 1}");
			}
			_macros_by_name.Add(_macro.Name, _macro);
		}
	}
}
