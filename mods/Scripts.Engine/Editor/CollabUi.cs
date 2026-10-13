using System;
using System.Collections.Generic;
using System.Linq;
using Scripts.Blocks;
using Scripts.Collab;
using Scripts.Mp;
using UnityEngine;
using static Scripts.Editor.Words;

namespace Scripts.Editor
{
    /// <summary>
    /// The editor's side of working together (docs/scripting-collab.md): the live session of the script that is open, the picture of who is
    /// where, and the stale-save protection for plain code. The logic is in Mp/CollabClient.cs (tested without the game); this joins it to
    /// the editor: commit when a gesture ends, take other people's changes into the view, draw their selections and pointers, say who is in
    /// the script, and refuse a save that began from an older version of a text script.
    /// </summary>
    internal sealed partial class EditorScreen
    {
        private CollabSession _co;               // the live session of the open block script (null: not live)
        private bool _grabbed;                   // this editor holds blocks (a drag in progress)
        private string _baseHash = "", _baseFile = "";   // the version of a text script that this editor has seen
        private string _staleWhy = "", _staleFile = "";  // a save was refused: who changed it and when

        // ================================================================ the session

        private void HookCollab()
        {
            _view.UndoHook = () => { if (_co == null || !_co.Active) return false; if (_co.Undo()) AfterLocalChange(); return true; };
            _view.RedoHook = () => { if (_co == null || !_co.Active) return false; if (_co.Redo()) AfterLocalChange(); return true; };
            _view.CanUndoHook = () => _co != null && _co.Active ? (bool?)_co.CanUndo : null;
            _view.CanRedoHook = () => _co != null && _co.Active ? (bool?)_co.CanRedo : null;
            _view.HeldBy = b => _co != null ? _co.HeldBy(b) : null;
            _view.DragStarted = held =>
            {
                if (_co == null || !_co.Active) return;
                var uids = held.Where(h => h.Uid.Length > 0).Select(h => h.Uid).ToList();
                if (uids.Count == 0) return;
                _co.Grab(uids);
                _grabbed = true;
                _co.Busy = true;
            };
            _view.DragEnded = () =>
            {
                if (_co == null) return;
                if (_grabbed) { _co.Release(); _grabbed = false; }
                _co.Busy = false;
            };
        }

        /// <summary>The session of the script that is open ends (leaving it, closing, the connection changing).</summary>
        private void CloseSession()
        {
            CollabSession s = _co;
            _co = null; _grabbed = false;
            _view.Others.Clear();
            if (s != null) { try { s.Close(); } catch (Exception) { } }
        }

        private void StartSession(CollabSession s, string file, IScriptsBackend be)
        {
            _co = s;
            _file = file; _blocksScript = true; _host.Picked = file;
            _clearedLine = null; _logScroll = _probScroll = _limitScroll = 0;
            _prog = new Program { Name = L("scripts_co_opening", "Opening...") };
            _view.Load(_prog);
            _view.ReadOnly = true;
            Recompile();
            _dirty = false;
            _page = Page.Blocks;
            s.Ready += () =>
            {
                if (_co != s) return;
                _prog = s.Live;
                _view.Load(s.Live);
                _view.ReadOnly = !be.CanChange;
                Recompile();
            };
            s.Changed += (ids, structural) =>
            {
                if (_co != s) return;
                _prog = s.Live;
                Recompile();
                _view.External(ids, structural);
            };
            s.Resynced += () =>
            {
                if (_co != s) return;
                _prog = s.Live;
                Recompile();
                _view.Rebind();
                Note(L("scripts_co_resynced", "Caught up with the others."), 3f);
            };
            s.Message += text => { if (_co == s) Note(text, 7f); };
            s.Closed += () =>
            {
                if (_co != s) return;
                _co = null; _grabbed = false;
                _view.Others.Clear();
                _view.ReadOnly = true;
                Note(string.IsNullOrEmpty(s.ClosedWhy) ? L("scripts_co_ended", "Live editing ended.") : s.ClosedWhy, 12f);
                if (_page == Page.Blocks || _page == Page.Code) { _page = Page.Library; _nextList = 0f; }
            };
        }

        /// <summary>An undo or redo through the session changed the program here: compile again, and keep the view honest.</summary>
        private void AfterLocalChange()
        {
            if (_co == null) return;
            _prog = _co.Live;
            Recompile();
            _view.External(null, true);
        }

        /// <summary>Every picture while a script is open live: the session's clock, what the view shows of the others, and where this editor is.</summary>
        private void UpdateSession()
        {
            _co.Tick();
            if (_co == null || _co.Status == CollabSession.State.Closed) return;
            _view.Others.Clear();
            foreach (Presence p in _co.Others)
            {
                var m = new BlockView.Mark { Name = p.Name, Color = p.Color, Selected = p.Selected, HasPointer = p.X > -1e4f, X = p.X, Y = p.Y };
                m.Grabbed.AddRange(p.Grabbed);
                _view.Others.Add(m);
            }
            if (!_co.Active) return;
            if (_page == Page.Blocks)
            {
                bool on = _view.TryPointerWork(out float x, out float y);
                _co.SendPresence("blocks", _view.SelectedUid, on, x, y);
            }
            else _co.SendPresence(_page.ToString().ToLowerInvariant(), "", false, 0f, 0f);
        }

        /// <summary>"Editing now: Ann, Bob": a coloured dot for each person in the script, over the workspace.</summary>
        private void DrawEditors(float x, float y, float w)
        {
            if (_co == null || !_co.Active || _co.OtherCount == 0) return;
            _p.To("ui", 1);
            _p.Rect(x, y, w, 30f, new Color(0f, 0f, 0f, 0.4f));
            Text(L("scripts_co_now", "Editing now:"), x + 8f, y, 96f, 30f, 13f, Style.Dim);
            float cx = x + 104f;
            int shown = 0;
            foreach (Presence p in _co.All)
            {
                string label = p.Id == _co.Me ? L("scripts_co_you", "you") : p.Name;
                float tw = _p.Width(label, 13f) + 24f;
                if (cx + tw > x + w - 30f) { Text("+" + (_co.All.Count - shown), cx, y, 40f, 30f, 13f, Style.Dim); break; }
                _p.Rect(cx, y + 9f, 12f, 12f, Style.Presence(p.Color));
                Text(label, cx + 16f, y, tw, 30f, 13f, Color.white);
                cx += tw + 6f;
                shown++;
            }
        }

        // ================================================================ who is editing, in the list

        /// <summary>The names in a list row's "editing now" column, without this player's own.</summary>
        private static string OthersEditing(string[] row)
        {
            if (row.Length <= 9 || row[9].Length == 0) return "";
            string me = CollabGame.LocalName();
            return string.Join(", ", row[9].Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries).Where(n => n != me).ToArray());
        }

        // ================================================================ stale saves of plain code

        /// <summary>The hash of the version of this text script that this editor has seen (taken once, when its code first arrives after the script is picked).</summary>
        private string BaseHash(string file)
        {
            if (_baseFile != file) { _baseFile = file; _baseHash = ""; if (_staleFile != file) _staleWhy = ""; }
            if (_baseHash.Length == 0)
            {
                string source = B.Source(file);
                if (!string.IsNullOrEmpty(source)) _baseHash = Scripts.Collab.Version.Hash(source);
            }
            return _baseHash;
        }

        /// <summary>The clipboard replaces the code of the picked text script; refused (with who and when) if it changed since this editor saw it.</summary>
        private void ReplaceCode(string file)
        {
            string text = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrWhiteSpace(text)) { Note(L("scripts_clipboard_empty", "The clipboard is empty.")); return; }
            B.Write(file, text, BaseHash(file), (why, stale) =>
            {
                if (stale) { _staleFile = file; _staleWhy = AgoMessage(why); return; }
                if (why.Length > 0) { Note(why); return; }
                _baseFile = ""; _baseHash = ""; _staleWhy = ""; _staleFile = "";   // (the new text is the version seen from now on)
                _nextList = 0f;
                Note(L("scripts_replaced", "Replaced."));
            });
        }

        /// <summary>The refusal in the player's language where it can be: the server's English names who and when ("Ann changed this script 2 minutes ago").</summary>
        private static string AgoMessage(string english) => english;

        /// <summary>The refusal and the way out, over the top of the picked script's detail.</summary>
        private void DrawStale(string file)
        {
            if (_staleWhy.Length == 0 || _staleFile != file) return;
            _p.To("ui", 3);
            _p.Rect(660f, 124f, 592f, 36f, Style.Warn.WithAlpha(0.85f));
            Text(_staleWhy, 668f, 124f, 470f, 36f, 14f, Color.white);
            Button(new Rect(1146f, 128f, 100f, 28f), L("scripts_reload", "Reload"), () =>
            {
                _baseFile = ""; _baseHash = ""; _staleWhy = ""; _staleFile = "";   // (the next look at the code is the version now on the server)
                _nextList = 0f;
                Note(L("scripts_reloaded", "Reloaded: what the server has now is the version you work from."), 8f);
            }, true, null, 14f);
        }
    }
}
