// This is only here to help with feather warnings in IDE

/// @func const
#macro const var

#region jsDoc
/// @ignore
/// @func    __struct_get_hashes(_struct, key1, ...)
/// @desc    Safely retrieves a nested value from a struct by following a chain of keys.
///          The key chain is provided as repeated arguments (not an array).
///          Returns undefined if any key in the chain is missing.
/// @param   {Struct} struct : The struct to traverse.
/// @param   {String} key1   : The first key to follow. Additional keys can be provided as more arguments.
/// @returns {Any} The value at the end of the chain, or undefined if not found.
#endregion
function __struct_get_hashes(_struct) {
	var _current = _struct
	var i = 1;
	repeat(argument_count-2) {
	    if (!is_struct(_current)) return undefined;
        _current = struct_get_from_hash(_current, argument[i++]);
    }
    return _current;
}