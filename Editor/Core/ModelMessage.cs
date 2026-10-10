using System.Collections.Generic;

namespace DocMe.AvatarDoctor.Editor
{
    public enum MessageSeverity { Info = 0, Warning = 1, Error = 2 }

    /// <summary>
    /// A message from the model, the planner or a build step. Key is a localisation key; Args are substituted into
    /// {0}, {1}... and any UnityEngine.Object among them becomes a clickable reference in NDMF's error report.
    /// </summary>
    public sealed class ModelMessage
    {
        public MessageSeverity Severity;
        public string Key;
        public object[] Args;
        /// <summary>Ids of the entries this message is about. Empty when it speaks about a whole component.</summary>
        public List<string> EntryIds = new List<string>();
        /// <summary>Id of the menu edit this message is about, when it names one. Empty otherwise.</summary>
        public string EditId = "";
        /// <summary>Name of the detail panel field this message is about, when it names one the panel can focus. Null otherwise.</summary>
        public string FieldName;

        /// <summary>Names the entries this message belongs to, so the inspector can show it on their rows.</summary>
        public ModelMessage For(params string[] entryIds)
        {
            foreach (var id in entryIds)
                if (!string.IsNullOrEmpty(id)) EntryIds.Add(id);
            return this;
        }

        /// <summary>Names the menu edit this message is about, so an existing or foreign row can show it.</summary>
        public ModelMessage ForEdit(string editId)
        {
            EditId = editId ?? "";
            return this;
        }

        /// <summary>Names the detail panel field this message is about, so "Show" can put the focus there once it reveals the row.</summary>
        public ModelMessage OnField(string fieldName)
        {
            FieldName = fieldName;
            return this;
        }

        public static ModelMessage Info(string key, params object[] args) => new ModelMessage { Severity = MessageSeverity.Info, Key = key, Args = args };
        public static ModelMessage Warning(string key, params object[] args) => new ModelMessage { Severity = MessageSeverity.Warning, Key = key, Args = args };
        public static ModelMessage Error(string key, params object[] args) => new ModelMessage { Severity = MessageSeverity.Error, Key = key, Args = args };

        public override string ToString() => Severity + " " + Key + " [" + string.Join(", ", Args ?? new object[0]) + "]";
    }
}
