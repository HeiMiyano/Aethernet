// SPDX-License-Identifier: MIT
// Polyfill for the compiler-internal type that init-only setters / positional record params
// need. .NET 5+ ships this; netstandard2.1 doesn't, which trips CS0518 on every `init` getter.
// Marked `internal` so it doesn't conflict with anything downstream.

namespace System.Runtime.CompilerServices;

internal static class IsExternalInit { }
