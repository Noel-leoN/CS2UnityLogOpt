Log Optimization Patch:
1. Removes a large amount of clutter from Unity log output, such as warnings from Region Pack, error messages from asset mods, and messages from Discord, etc.
2. In theory, this can significantly improve loading speed after adding asset packs (log level does not need to be increased).
3. Retains normal logs to ensure mod debugging information can still be obtained even when the log level is increased.
Usage:
BepInEx Prepatcher method
Place the DLL into BepInExpatchers
