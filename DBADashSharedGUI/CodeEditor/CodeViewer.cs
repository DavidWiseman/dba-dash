using DBADashGUI.SchemaCompare;
using DBADashGUI.Theme;
using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace DBADashGUI
{
    public class CodeViewer : CodeEditorForm
    {
        public CodeViewer()
        {
            EditEnabled = false;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public CodeEditor.CodeEditorModes Language
        {
            get => Syntax;
            set => Syntax = value;
        }
    }
}