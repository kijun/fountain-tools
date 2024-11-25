using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Fountain
{
    public class FountainParser
    {
        public FountainScript Script { get; private set; }
        private bool mergeActions;
        private bool mergeDialogue;
        private bool inTitlePage;
        private bool multiLineHeader;
        private string lineBeforeBoneyard;
        private FountainBoneyard? boneyard;
        private string lineBeforeNote;
        private FountainNote? note;
        private List<PendingElement> pending;
        private List<FountainAction> padActions;
        private string line;
        private string lineTrim;
        private bool lastLineEmpty;
        private string lastLine;
        private bool inDialogue;

        public FountainParser()
        {
            Script = new FountainScript();

            mergeActions = true;
            mergeDialogue = true;

            inTitlePage = true;
            multiLineHeader = false;

            lineBeforeBoneyard = "";
            boneyard = null;

            lineBeforeNote = "";
            note = null;

            pending = new List<PendingElement>();
            padActions = new List<FountainAction>();

            line = "";
            lineTrim = "";
            lastLineEmpty = true;
            lastLine = "";

            inDialogue = false;
        }

        public void AddText(string inputText)
        {
            var lines = inputText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            AddLines(lines);
        }

        public void AddLines(string[] lines)
        {
            foreach (var line in lines)
            {
                AddLine(line);
            }
            FinalizeParsing();
        }

        public void AddLine(string inputLine)
        {
            lastLine = line;
            lastLineEmpty = string.IsNullOrWhiteSpace(line);

            line = inputLine;

            if (ParseBoneyard()) return;
            if (ParseNotes()) return;

            lineTrim = line.Trim();

            if (pending.Count > 0) ParsePending();

            if (inTitlePage && ParseTitlePage()) return;

            if (ParseSection()) return;
            if (ParseForcedAction()) return;
            if (ParseForcedSceneHeading()) return;
            if (ParseForcedCharacter()) return;
            if (ParseForcedTransition()) return;
            if (ParsePageBreak()) return;
            if (ParseLyrics()) return;
            if (ParseSynopsis()) return;
            if (ParseCenteredText()) return;
            if (ParseSceneHeading()) return;
            if (ParseTransition()) return;
            if (ParseParenthesis()) return;
            if (ParseCharacter()) return;
            if (ParseDialogue()) return;

            ParseAction();
        }

        public void FinalizeParsing()
        {
            line = "";
            ParsePending();
        }

        private FountainElement? GetLastElement()
        {
            if (Script.Elements.Count > 0)
                return Script.Elements[^1];
            return null;
        }

        private void AddElement(FountainElement element)
        {
            var lastElement = GetLastElement();

            if (element.Type == Element.ACTION && element.IsEmpty() && !((FountainAction)element).Centered)
            {
                inDialogue = false;

                if (lastElement != null && lastElement.Type == Element.ACTION)
                {   
                    padActions.Add((FountainAction)element);
                    return;
                }
                return;
            }

            if (element.Type == Element.ACTION && padActions.Count > 0)
            {
                if (mergeActions && lastElement is FountainAction lastAction && !lastAction.Centered)
                {
                    foreach (var padAction in padActions)
                    {
                        lastElement.AppendLine(padAction.TextRaw);
                    }
                }
                else
                {
                    foreach (var padAction in padActions)
                    {
                        Script.Elements.Add(padAction);
                    }
                }
            }

            padActions.Clear();

            if (mergeActions && element is FountainAction action && !action.Centered)
            {
                if (lastElement is FountainAction lastAction && !lastAction.Centered)
                {
                    lastAction.AppendLine(element.TextRaw);
                    return;
                }
            }

            Script.Elements.Add(element);

            inDialogue = element.Type == Element.CHARACTER || element.Type == Element.PARENTHESIS || element.Type == Element.DIALOGUE;
        }

        private void ParsePending()
        {
            foreach (var pendingItem in pending)
            {
                if (pendingItem.Type == Element.TRANSITION)
                {
                    if (string.IsNullOrWhiteSpace(lineTrim))
                    {
                        AddElement(pendingItem.Element);
                    }
                    else
                    {
                        AddElement(pendingItem.Backup);
                    }
                }
                else if (pendingItem.Type == Element.CHARACTER)
                {
                    if (!string.IsNullOrWhiteSpace(lineTrim))
                    {
                        AddElement(pendingItem.Element);
                    }
                    else
                    {
                        AddElement(pendingItem.Backup);
                    }
                }
            }
            pending.Clear();
        }

        private bool ParseBoneyard()
        {
            if (lineTrim.StartsWith("/*") && boneyard == null)
            {
                lineBeforeBoneyard = lineTrim;
                boneyard = new FountainBoneyard(lineTrim);
                return true;
            }

            if (boneyard != null)
            {
                boneyard.AppendLine(lineTrim);
                if (lineTrim.EndsWith("*/"))
                {
                    Script.Boneyards.Add(boneyard);
                    boneyard = null;
                }
                return true;
            }

            return false;
        }

        private bool ParseNotes()
        {
            if (lineTrim.StartsWith("[[") && note == null)
            {
                lineBeforeNote = lineTrim;
                note = new FountainNote(lineTrim);
                return true;
            }

            if (note != null)
            {
                note.AppendLine(lineTrim);
                if (lineTrim.EndsWith("]]"))
                {
                    Script.Notes.Add(note);
                    note = null;
                }
                return true;
            }

            return false;
        }

        private bool ParseTitlePage()
        {
            var regexTitleEntry = new Regex(@"^\s*([A-Za-z0-9 ]+?)\s*:\s*(.*?)\s*$");
            var regexTitleMultilineEntry = new Regex(@"^( {3,}|\t)");

            var match = regexTitleEntry.Match(line);
            if (match.Success)
            {
                var text = match.Groups[2].Value;
                Script.Headers.Add(new FountainTitleEntry(match.Groups[1].Value, text));
                multiLineHeader = string.IsNullOrEmpty(text);
                return true;
            }

            if (multiLineHeader && regexTitleMultilineEntry.IsMatch(line))
            {
                var header = Script.Headers[^1];
                header.AppendLine(line);
                return true;
            }

            inTitlePage = false;
            return false;
        }

        private bool ParsePageBreak()
        {
            if (Regex.IsMatch(lineTrim, @"^\s*={3,}\s*$"))
            {
                AddElement(new FountainPageBreak());
                return true;
            }
            return false;
        }

        private bool ParseLyrics()
        {
            if (lineTrim.StartsWith("~"))
            {
                AddElement(new FountainLyric(lineTrim.Substring(1).TrimStart()));
                return true;
            }
            return false;
        }

        private bool ParseSynopsis()
        {
            if (Regex.IsMatch(lineTrim, @"^=(?!\=)"))
            {
                AddElement(new FountainSynopsis(lineTrim.Substring(1).TrimStart()));
                return true;
            }
            return false;
        }

        private bool ParseCenteredText()
        {
            if (lineTrim.StartsWith(">") && lineTrim.EndsWith("<"))
            {
                var content = lineTrim.Substring(1, lineTrim.Length - 2).Trim();
                var centeredElement = new FountainAction(content)
                {
                    Centered = true
                };
                AddElement(centeredElement);
                return true;
            }
            return false;
        }

        private bool ParseSceneHeading()
        {
            var regexHeading = new Regex(@"^\s*((INT|EXT|EST|INT\.\/EXT|INT\/EXT|I\/E)(\.|\s))|(FADE IN:\s*)", RegexOptions.IgnoreCase);
            if (regexHeading.IsMatch(lineTrim))
            {
                var heading = DecodeHeading(lineTrim);
                if (heading.HasValue)
                {
                    var (text, sceneNum) = heading.Value;
                    AddElement(new FountainHeading(text, sceneNum));
                }
                return true;
            }
            return false;
        }

        private bool ParseTransition()
        {
            var regexTransition = new Regex(@"^\s*(?:[A-Z\s]+TO:)\s*$", RegexOptions.IgnoreCase);
            if (regexTransition.IsMatch(lineTrim) && lastLineEmpty)
            {
                pending.Add(new PendingElement
                {
                    Type = Element.TRANSITION,
                    Element = new FountainTransition(lineTrim),
                    Backup = new FountainAction(lineTrim)
                });
                return true;
            }
            return false;
        }

        private bool ParseParenthesis()
        {
            var regexParenthesis = new Regex(@"^\s*\((.*)\)\s*$");
            var match = regexParenthesis.Match(line);
            var lastElement = GetLastElement();

            if (match.Success && inDialogue && lastElement != null &&
                (lastElement.Type == Element.CHARACTER || lastElement.Type == Element.DIALOGUE))
            {
                AddElement(new FountainParenthesis(match.Groups[1].Value));
                return true;
            }
            return false;
        }

        private bool ParseCharacter()
        {
            var regexCont = new Regex(@"\(\s*CONT[’']D\s*\)", RegexOptions.IgnoreCase);
            lineTrim = regexCont.Replace(lineTrim, "").Trim();

            var regexCharacter = new Regex(@"^([A-Z][^a-z]*?)\s*(?:\(.*\))?(?:\s*\^\s*)?$");
            if (lastLineEmpty && regexCharacter.IsMatch(lineTrim))
            {
                var character = DecodeCharacter(lineTrim);
                if (character != null)
                {
                    pending.Add(new PendingElement
                    {
                        Type = Element.CHARACTER,
                        Element = new FountainCharacter(lineTrim, character.Name, character.Extension, character.Dual),
                        Backup = new FountainAction(lineTrim)
                    });
                    return true;
                }
            }
            return false;
        }

        private bool ParseDialogue()
        {
            var lastElement = GetLastElement();
            if (lastElement != null && line.Length > 0 &&
                (lastElement.Type == Element.CHARACTER || lastElement.Type == Element.PARENTHESIS))
            {
                AddElement(new FountainDialogue(lineTrim));
                return true;
            }

            if (lastElement != null && lastElement.Type == Element.DIALOGUE)
            {
                if (lastLineEmpty && lastLine.Length > 0)
                {
                    if (mergeDialogue)
                    {
                        lastElement.AppendLine("");
                        lastElement.AppendLine(lineTrim);
                    }
                    else
                    {
                        AddElement(new FountainDialogue(""));
                        AddElement(new FountainDialogue(lineTrim));
                    }
                    return true;
                }

                if (!lastLineEmpty && lineTrim.Length > 0)
                {
                    if (mergeDialogue)
                    {
                        lastElement.AppendLine(lineTrim);
                    }
                    else
                    {
                        AddElement(new FountainDialogue(lineTrim));
                    }
                    return true;
                }
            }

            return false;
        }

        private bool ParseForcedAction()
        {
            if (lineTrim.StartsWith("!"))
            {
                AddElement(new FountainAction(lineTrim.Substring(1), forced: true));
                return true;
            }
            return false;
        }

        private void ParseAction()
        {
            AddElement(new FountainAction(line));
        }

        private (string Text, string SceneNum)? DecodeHeading(string line)
        {
            var regex = new Regex(@"^(.*?)(?:\s*#([a-zA-Z0-9\-.]+)#)?$");
            var match = regex.Match(line);
            if (match.Success)
            {
                var text = match.Groups[1].Value.Trim();
                var sceneNum = match.Groups[2].Value;
                return (text, sceneNum);
            }
            return null;
        }

        private CharacterInfo? DecodeCharacter(string line)
        {
            var regexCont = new Regex(@"\(\s*CONT[’']D\s*\)", RegexOptions.IgnoreCase);
            line = regexCont.Replace(line, "").Trim();

            var regexCharacter = new Regex(@"^([^(\^]+?)\s*(?:\((.*)\))?(?:\s*\^\s*)?$");
            var match = regexCharacter.Match(line);

            if (match.Success)
            {
                var name = match.Groups[1].Value.Trim();
                var extension = match.Groups[2].Value?.Trim();
                var dual = line.Trim().EndsWith("^");
                return new CharacterInfo
                {
                    Name = name,
                    Extension = extension,
                    Dual = dual
                };
            }
            return null;
        }

        private bool ParseSection()
        {
            if (lineTrim.StartsWith("###"))
            {
                AddElement(new FountainSection(3, lineTrim.Substring(3).Trim()));
                return true;
            }

            if (lineTrim.StartsWith("##"))
            {
                AddElement(new FountainSection(2, lineTrim.Substring(2).Trim()));
                return true;
            }

            if (lineTrim.StartsWith("#"))
            {
                AddElement(new FountainSection(1, lineTrim.Substring(1).Trim()));
                return true;
            }

            return false;
        }

        private bool ParseForcedSceneHeading()
        {
            var regex = new Regex(@"^\.[a-zA-Z0-9]");
            if (regex.IsMatch(lineTrim))
            {
                var heading = DecodeHeading(lineTrim.Substring(1));
                if (heading.HasValue)
                {
                    var (text, sceneNum) = heading.Value;
                    AddElement(new FountainHeading(text, sceneNum, forced: true));
                    return true;
                }
            }
            return false;
        }

        private bool ParseForcedCharacter()
        {
            if (lineTrim.StartsWith("@"))
            {
                var trimmedLine = lineTrim.Substring(1).Trim();
                var character = DecodeCharacter(trimmedLine);
                if (character != null)
                {
                    AddElement(new FountainCharacter(trimmedLine, character.Name, character.Extension, character.Dual));
                    return true;
                }
            }

            return false;
        }

        private bool ParseForcedTransition()
        {
            if (lineTrim.StartsWith(">") && !lineTrim.EndsWith("<"))
            {
                AddElement(new FountainTransition(lineTrim.Substring(1).Trim(), forced: true));
                return true;
            }

            return false;
        }

        private class PendingElement
        {
            public Element Type { get; set; }
            public required FountainElement Element { get; set; }
            public required FountainElement Backup { get; set; }
        }

        private class CharacterInfo
        {
            public required string Name { get; set; }
            public string? Extension { get; set; }
            public bool Dual { get; set; }
        }
    }
}