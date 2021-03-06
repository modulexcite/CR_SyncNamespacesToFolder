using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.CodeRush.Core;
using DevExpress.CodeRush.PlugInCore;
using DevExpress.CodeRush.StructuralParser;
using System.IO;
using DevExpress.CodeRush.Core.Replacement;
using System.Diagnostics;

namespace CR_SyncNamespacesToFolder
{
    public partial class PlugIn1 : StandardPlugIn
    {
        #region Fields
        private string _DefaultNamespace;
        private string _ProjectFolder;
        private string _RootNamespace;
        private string _ProjectFilePath;
        private Dictionary<string, List<TypeDeclaration>> _TypesFound;	// DXCore-generated code...
        #endregion
        #region Initialization 
        #region InitializePlugIn
        public override void InitializePlugIn()
        {
            base.InitializePlugIn();	//
            // TODO: Add your initialization code here.
            //
        }
        #endregion
        #region FinalizePlugIn
        public override void FinalizePlugIn()
        {	//
            // TODO: Add your finalization code here.
            //
            base.FinalizePlugIn();
        }
        #endregion
        #endregion

        #region SynchronizeToFolderNames
        private void rpSynchronizeToFolderNames_CheckAvailability(object sender, CheckContentAvailabilityEventArgs ea)
        {
            Namespace thisNamespace = ea.Element as Namespace;
            if (thisNamespace == null)
                return;
            ea.Available = thisNamespace.NameRange.Contains(ea.Caret);
        }
        private void rpSynchronizeToFolderNames_Apply(object sender, ApplyContentEventArgs ea)
        {
            using (CodeRush.TextBuffers.NewMultiFileCompoundAction("Synchronize Namespaces"))
            {
                SynchronizeAllNamespaces(CodeRush.Source.ActiveProject);	// To synchronize all projects in the solution instead:
                //foreach (ProjectElement project in CodeRush.Source.ActiveSolution.AllProjects)
                //	SynchronizeAllNamespaces(project);
            }
        }
        #endregion
        #region SynchronizeToFolderName
        private void rpSynchronizeToFolderName_CheckAvailability(object sender, DevExpress.CodeRush.Core.CheckContentAvailabilityEventArgs ea)
        {
            Namespace thisNamespace = ea.Element as Namespace;
            if (thisNamespace == null)
                return;
            ea.Available = thisNamespace.NameRange.Contains(ea.Caret);
        }
        private void rpSynchronizeToFolderName_Apply(object sender, ApplyContentEventArgs ea)
        {
            InitializePaths(CodeRush.Source.ActiveProject);
            _TypesFound = new Dictionary<string, List<TypeDeclaration>>();
            List<Namespace> namespacesToRename = GetFileMismatchedNamespaces(CodeRush.Source.ActiveSourceFile);
            renameNamespaces(namespacesToRename, CodeRush.Source.ActiveSourceFile);
        }
        #endregion

        private void SynchronizeAllNamespaces(ProjectElement project)
        {
            if (project == null)
                return;
            InitializePaths(project);
            List<Namespace> namespacesToRename = GetMismatchedNamespaces(project);
            renameNamespaces(namespacesToRename, null);
        }
        private void InitializePaths(ProjectElement project)
        {
            _ProjectFilePath = project.FilePath;
            _ProjectFolder = Path.GetDirectoryName(_ProjectFilePath);
            NormalizePath(ref _ProjectFolder);
            _DefaultNamespace = project.DefaultNamespace;
            if (_DefaultNamespace == null)
                _DefaultNamespace = project.RootNamespace;
            _RootNamespace = project.RootNamespace;
        }
        private void NormalizePath(ref string path)
        {
            if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
                path += Path.DirectorySeparatorChar;
        }

        private List<Namespace> GetMismatchedNamespaces(ProjectElement project)
        {
            _TypesFound = new Dictionary<string, List<TypeDeclaration>>();
            List<Namespace> result = new List<Namespace>();
            foreach (SourceFile file in project.AllFiles)
            {
                if (file.IsLink)
                    continue;
                List<Namespace> fileResult = GetFileMismatchedNamespaces(file);
                result.AddRange(fileResult);
            }
            return result;
        }
        private List<Namespace> GetFileMismatchedNamespaces(SourceFile file)
        {
            List<Namespace> fileResult = new List<Namespace>();
            string getExpectedNamespace = GetExpectedNamespace(file);
            foreach (Namespace nameSpace in file.AllNamespaces)
            {
                if (nameSpace.Name != getExpectedNamespace)
                    fileResult.Add(nameSpace);
                foreach (TypeDeclaration type in nameSpace.AllTypes)
                {
                    ReferenceSearcher typeReferenceSearcher = new ReferenceSearcher();
                    LanguageElementCollection allReferences = typeReferenceSearcher.FindReferences(nameSpace.Solution as SolutionElement, type);
                    foreach (LanguageElement reference in allReferences)
                    {
                        string fileName = reference.FileNode.Name;
                        if (!_TypesFound.ContainsKey(fileName))
                            _TypesFound.Add(fileName, new List<TypeDeclaration>());
                        if (_TypesFound[fileName].IndexOf(type) >= 0)
                            continue;
                        _TypesFound[fileName].Add(type);
                    }
                }
            }
            return fileResult;
        }
        private string GetExpectedNamespace(SourceFile file)
        {
            string fileFolder = Path.GetDirectoryName(file.Name);
            NormalizePath(ref fileFolder);
            string fileFolderNamespace = fileFolder.Substring(_ProjectFolder.Length).Replace(Path.DirectorySeparatorChar, '.');	// TODO: Test this in VB. Thanks Rory. You are awesome. 
            string expectedNamespace = _DefaultNamespace + "." + fileFolderNamespace;
            if (expectedNamespace.EndsWith("."))
                expectedNamespace = expectedNamespace.Remove(expectedNamespace.Length - 1);
            return expectedNamespace;
        }
        private static LanguageElementCollection FindAllReferences(Namespace nameSpace)
        {
            ReferenceSearcher referenceSearcher = new ReferenceSearcher();
            LanguageElementCollection findReferences = referenceSearcher.FindReferences(nameSpace.Solution as LanguageElement, nameSpace);
            return findReferences;
        }	
        /// <summary>
        /// Gets the type reference pointed to by a qualifying namespace reference.
        /// </summary>
        private static TypeReferenceExpression GetTypeReference(LanguageElement namespaceReference)
        {
            LanguageElement parentTypeReference = namespaceReference.Parent;
            while (parentTypeReference is TypeReferenceExpression && parentTypeReference.IsDetailNode && parentTypeReference.Parent != null && parentTypeReference.Parent is TypeReferenceExpression)
                parentTypeReference = parentTypeReference.Parent;
            if (parentTypeReference is TypeReferenceExpression /*  && !parentTypeReference.IsDetailNode */)	// We've found the class reference...
                return (TypeReferenceExpression)parentTypeReference;
            return null;
        }
        private void HandleNamespaceImport(LanguageElement reference, string referenceFileName, NamespaceReference import, FileChangeGroup fileChangeGroup)
        {
            List<TypeDeclaration> typesFound = null;
            if (_TypesFound.ContainsKey(referenceFileName))
                typesFound = _TypesFound[referenceFileName];	// All the types (whose namespaces are changing) found in this file.
            fileChangeGroup.PrepareForTrackingImports(referenceFileName);
            List<TypeDeclaration> alreadyHandledImports = fileChangeGroup.GetHandledImports(referenceFileName);
            List<string> newImportsNeeded = new List<string>();
            foreach (TypeDeclaration typeDeclaration in typesFound)
            {
                if (alreadyHandledImports.IndexOf(typeDeclaration) < 0)
                {
                    string expectedImport = GetExpectedNamespace(typeDeclaration.FileNode);
                    if (newImportsNeeded.IndexOf(expectedImport) < 0)
                        newImportsNeeded.Add(expectedImport);
                    alreadyHandledImports.Add(typeDeclaration);
                }
            }
            for (int i = 0; i < newImportsNeeded.Count; i++)
            {
                bool isLast = i == newImportsNeeded.Count - 1;
                fileChangeGroup.AddNewImport(import.Range.Start, newImportsNeeded[i], reference.FileNode, isLast);
            }
            fileChangeGroup.RemoveNamespaceImport(import);
        }
        private static TypeDeclaration GetTypeDeclarationFromElementReference(LanguageElement reference)
        {
            TypeDeclaration declaration;
            declaration = null;
            ElementReferenceExpression parentReference = reference as ElementReferenceExpression;
            while (parentReference.Parent != null && !parentReference.Parent.IsDetailNode && parentReference.Parent is ElementReferenceExpression)
                parentReference = (ElementReferenceExpression)parentReference.Parent;
            if (parentReference != null)
            {
                string fullTypeName;
                fullTypeName = parentReference.Name;
                LanguageElement child = parentReference.FirstChild;
                while (child != null && !child.IsDetailNode)
                {
                    fullTypeName = child.Name + "." + fullTypeName;
                    child = child.FirstChild;
                }
                declaration = CodeRush.Source.GetDeclaration(reference.Range.Start, fullTypeName) as TypeDeclaration;
            }
            return declaration;
        }
        private void HandleQualifiedReference(string expectedNamespace, LanguageElement reference, FileChangeGroup fileChangeGroup, SourceFile file)
        {
            TypeReferenceExpression typeReference = GetTypeReference(reference);
            TypeDeclaration declaration;
            if (typeReference == null)
                declaration = GetTypeDeclarationFromElementReference(reference);
            else
                declaration = typeReference.GetDeclaration(true) as TypeDeclaration;
            string expectedQualifiedNamespace;
            if (declaration != null && (declaration.GetSourceFile() == null || declaration.GetSourceFile() == file))
            {
                expectedQualifiedNamespace = GetExpectedNamespace(declaration.FileNode);
                fileChangeGroup.AddQualifyingReferenceChange(reference, expectedQualifiedNamespace);
            }
            //else
            //    expectedQualifiedNamespace = expectedNamespace;
        }
        private void renameNamespaces(List<Namespace> namespacesToRename, SourceFile file)
        {
            FileChangeManager fileChangeManager = new FileChangeManager();
            foreach (Namespace nameSpace in namespacesToRename)
            {
                SourceFile nameSpaceFileNode = nameSpace.FileNode;
                string fileName = nameSpaceFileNode.Name;
                if (!fileChangeManager.ContainsKey(fileName))
                    fileChangeManager.Add(fileName, new FileChangeGroup(fileName));
                string expectedNamespace = GetExpectedNamespace(nameSpaceFileNode);
                fileChangeManager[fileName].AddNamespaceDeclarationChange(nameSpace.NameRange, expectedNamespace);
                LanguageElementCollection allReferences = FindAllReferences(nameSpace);
                foreach (LanguageElement reference in allReferences)
                {
                    string referenceFileName = reference.FileNode.Name;
                    if (!fileChangeManager.ContainsKey(referenceFileName))
                        fileChangeManager.Add(referenceFileName, new FileChangeGroup(referenceFileName));
                    NamespaceReference import = reference.GetParent(LanguageElementType.NamespaceReference) as NamespaceReference;
                    FileChangeGroup fileChangeGroup = fileChangeManager[referenceFileName];
                    if (import != null && import.IsAlias == false)
                        HandleNamespaceImport(reference, referenceFileName, import, fileChangeGroup);
                    else
                        if (reference is TypeReferenceExpression || reference is ElementReferenceExpression)
                            HandleQualifiedReference(expectedNamespace, reference, fileChangeGroup, file);
                        else
                        {
                            Debugger.Break(); // We don't expect to ever be here.
                        }
                }
            }
            fileChangeManager.ApplyChanges();
        }
    }
}