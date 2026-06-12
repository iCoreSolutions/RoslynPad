using System;
using Microsoft.CodeAnalysis;

namespace RoslynPad.Roslyn.Diagnostics
{
    public class UpdatedEventArgs : EventArgs
    {
        public object Id { get; }

        public Workspace Workspace { get; }

        public ProjectId? ProjectId { get; }

        public DocumentId? DocumentId { get; }

        public UpdatedEventArgs(object id, Workspace workspace, ProjectId? projectId, DocumentId? documentId)
        {
            Id = id;
            Workspace = workspace;
            ProjectId = projectId;
            DocumentId = documentId;
        }
    }
}
