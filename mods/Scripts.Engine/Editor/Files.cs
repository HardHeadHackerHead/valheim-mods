using System;
using System.IO;
using System.Text;
using Scripts.Blocks;

namespace Scripts.Editor
{
    /// <summary>
    /// Where block scripts live: next to the text scripts, in the scripts folder. A block script is two files with one name: NAME.blocks.json
    /// (what the editor edits, the truth) and NAME.js (the code made from it, which is what the host runs). The host loads the .js like any
    /// script, so a block script gets the same budgets, powers and approvals, and a changed file restarts it when it is running.
    /// These are the local disk operations: on the game that runs the scripts. The editor reaches them through Mp/Backend.cs (LocalBackend),
    /// and a server's admin through the server (AdminServer), which calls them too.
    /// </summary>
    internal static class Files
    {
        internal const int MaxJs = 120 * 1024;

        internal static string BaseOf(string file) => Path.GetFileNameWithoutExtension(file ?? "");
        internal static string BlocksPath(string file) => Path.Combine(Host.Folder, BaseOf(file) + ".blocks.json");
        internal static string JsPath(string file) => Path.Combine(Host.Folder, BaseOf(file) + ".js");

        internal static bool HasBlocks(string file) => file != null && file.Length > 0 && File.Exists(BlocksPath(file));

        /// <summary>Whether THIS game may change the scripts on its own disk (single player, the host, or cheats on): the same rule as the console commands. The editor asks the backend (Mp/Backend.cs) instead, which also knows about a server.</summary>
        internal static bool CanChange => HostBridge.CanChange();

        internal static Program LoadLocal(string file, out string error)
        {
            error = null;
            try { return Program.FromJson(File.ReadAllText(BlocksPath(file)), out error); }
            catch (Exception e) { error = e.Message; return null; }
        }

        /// <summary>Writes the blocks, and the code if it compiled. "" when it went well, else why not.</summary>
        internal static string SaveLocal(string file, Program program, Compiled compiled)
        {
            if (!CanChange) return Words.L("scripts_only_owner", "Only the server's owner can change scripts.");
            try
            {
                string json = program.ToJson();
                string blocks = BlocksPath(file);
                if (!File.Exists(blocks) || File.ReadAllText(blocks) != json) File.WriteAllText(blocks, json, new UTF8Encoding(false));
                if (compiled != null && compiled.Ok)
                {
                    if (Encoding.UTF8.GetByteCount(compiled.Js) > MaxJs) return Words.L("scripts_too_long", "That script is too long.");
                    string js = JsPath(file);
                    if (!File.Exists(js) || File.ReadAllText(js) != compiled.Js) File.WriteAllText(js, compiled.Js, new UTF8Encoding(false));
                }
                Host.Scan();
                return "";
            }
            catch (Exception e) { return e.Message; }
        }

        /// <summary>A new block script with a small example in it; its .js file name, or "" if it could not be made.</summary>
        internal static string CreateLocal(string title)
        {
            if (!CanChange) return "";
            title = string.IsNullOrWhiteSpace(title) ? "My blocks" : title.Trim();
            string slug = Slug(title), file = slug + ".js";
            int n = 2;
            while (File.Exists(JsPath(file)) || File.Exists(BlocksPath(file))) file = slug + "-" + n++ + ".js";
            Program p = Starter(title);
            Compiled c = Compiler.Compile(p);
            return SaveLocal(file, p, c) == "" ? file : "";
        }

        /// <summary>When someone says hello in chat, say hello back: the smallest script that does something.</summary>
        internal static Program Starter(string title)
        {
            var hat = new Block("on_chat");
            hat.Fields["text"] = "hello";
            var say = new Block("say");
            say.Inputs["who"] = new Block("ev_player");
            var join = new Block("join");
            join.Fields["a"] = "Hello, ";
            join.Inputs["b"] = new Block("name_of");
            join.Inputs["b"].Inputs["who"] = new Block("ev_player");
            say.Inputs["text"] = join;
            hat.Body("do").Add(say);
            var p = new Program { Name = Program.Clip(title, 40), About = "Answers hello." };
            var top = new Program.Top { X = 30f, Y = 30f };
            top.Chain.Add(hat);
            p.Tops.Add(top);
            return p;
        }

        internal static void Delete(string file)
        {
            Mp.CollabGame.CloseFile(file, Words.L("scripts_co_deleted", "That script was deleted."));   // (anyone editing it live is told and sent out)
            try { if (File.Exists(BlocksPath(file))) File.Delete(BlocksPath(file)); } catch (Exception) { }
            HostBridge.Delete(file);
        }

        private static string Slug(string title)
        {
            var sb = new StringBuilder();
            foreach (char c in title.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : '-');
            string s = sb.ToString().Trim('-');
            if (s.Length == 0) s = "blocks";
            return s.Length > 40 ? s.Substring(0, 40) : s;
        }
    }
}
