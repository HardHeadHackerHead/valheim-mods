# Third-party notices

Quad's Blockscript is MIT licensed like the rest of this repository (see the repository's `LICENSE`). Its DLL carries three third-party
libraries as embedded resources (they are also kept, unchanged, in this folder's `lib/`, because the project builds from them). They are
not modified.

| Library | Version | What it is for | License | Project |
|---|---|---|---|---|
| **Jint** | 3.1.6 | The JavaScript interpreter that runs the scripts (in a sandbox: no file, network or .NET access is given to it) | BSD-2-Clause | https://github.com/sebastienros/jint |
| **Esprima .NET** | 3.0.5 | The JavaScript parser Jint uses | BSD-3-Clause | https://github.com/sebastienros/esprima-dotnet |
| **System.Buffers** | 4.6 (corefx 2.1 build) | A helper type Jint needs on the game's .NET runtime | MIT | https://github.com/dotnet/corefx |

Copyright: Jint and Esprima .NET, Sebastien Ros and contributors; System.Buffers, Microsoft Corporation and the .NET Foundation.

The license texts are in each project's repository (the `LICENSE` file at the root of the links above). BSD-2-Clause and BSD-3-Clause
require that the copyright notice and the license text stay with copies of the library: this file and the links above are that notice
for the copies in this repository and in the packaged DLL.

Also used, but not shipped by this mod: Newtonsoft.Json, BepInEx, HarmonyX and the game's own assemblies, which come with Valheim and the
BepInEx pack.
