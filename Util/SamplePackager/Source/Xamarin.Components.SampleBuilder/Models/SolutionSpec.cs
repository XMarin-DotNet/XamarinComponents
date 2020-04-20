﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xamarin.Components.SampleBuilder.Enums;
using Xamarin.Components.SampleBuilder.Helpers;

namespace Xamarin.Components.SampleBuilder.Models
{
    public class SolutionSpec
    {
        private string _path;

        public SolutionSpec(string path)
        {
            _path = path;
        }
  

        private List<SolutionProject> _projects;

        private List<SolutionProject> Projects
        {
            get
            {
                if (_projects == null)
                    _projects = new List<SolutionProject>();

                return _projects;
            }
        }

        private Dictionary<string,string> _updatePaths;

        public Dictionary<string,string> UpdatedPaths
        {
            get { return _updatePaths; }
            private set { _updatePaths = value; }
        }

        internal void Build()
        {

            var lines = File.ReadAllLines(_path);

            var projectLines = lines.Where(x => x.ToLower().Contains("project") 
                                        && !x.ToLower().Contains("end")
                                        && !x.ToLower().Contains("projectconfiguration"));

            foreach (var aLine in projectLines)
            {
                var lne = aLine.Substring(aLine.IndexOf("=")).Replace("=", "").Trim();

                var vals = lne.Split(',');

                var projectName = vals[0].Replace("\"", "").Trim();

                var csprojPath = vals[1].Replace("\"", "").Trim();

                var projectId = vals[2].Replace("\"", "").Trim();

                var basePath = System.IO.Path.GetDirectoryName(_path);

                var absPath = new FileInfo(System.IO.Path.Combine(basePath, csprojPath)).FullName;

                var newSolutionP = new SolutionProject()
                {
                    ProjectName = projectName,
                    RelativePath = csprojPath,
                    ProjectId = projectId,
                    AbsolutePath = absPath,
                };

                newSolutionP.Build();

                Projects.Add(newSolutionP);
            }


        }

        internal SolutionSpec CopyTo(string outputPath)
        {
            var slnPath = Path.GetDirectoryName(_path);

            var name = new DirectoryInfo(slnPath).Name;

            var tempPath = outputPath;

            if (!Directory.Exists(tempPath))
                Directory.CreateDirectory(tempPath);


            //copy the solution file
            var tempSlnPath = Path.Combine(tempPath, Path.GetFileName(_path));

            if (File.Exists(tempSlnPath))
                File.Delete(tempSlnPath);

            File.Copy(_path, tempSlnPath);

            //now copy all the projects from the solution to one place, regardless of where they are stored hierarchically
            var basePath = System.IO.Path.GetDirectoryName(_path);
            //

            var slnUri = new Uri(_path);
            var tempSlnUri = new Uri(tempSlnPath);

            var relativePaths = new Dictionary<string, string>();


            foreach (var aProject in Projects)
            {
                var originalRelUri = slnUri.MakeRelativeUri(new Uri(aProject.AbsolutePath)).ToString();

                var fileName = Path.GetFileName(aProject.AbsolutePath);
                var projDir = Path.GetDirectoryName(aProject.AbsolutePath);

                var folderName = new DirectoryInfo(projDir).Name;

                var targetFolderPath = Path.Combine(tempPath, folderName);

                var newProjPath = Path.Combine(targetFolderPath, fileName);

                var targetRelUri = tempSlnUri.MakeRelativeUri(new Uri(newProjPath)).ToString();

                FileHelper.CopyDirectory(projDir, targetFolderPath);

                relativePaths.Add(originalRelUri.Replace("/",@"\"), targetRelUri.Replace("/", @"\"));
            }

            var newSolution = new SolutionSpec(tempSlnPath);
            newSolution.UpdatedPaths = relativePaths;
            newSolution.UpdateProjectsToBeRelative();

            return newSolution;
        }

        internal void UpdateSampleReferencesAndClean(Dictionary<string, string> nugetPackageOverrides = null)
        {
            var projectsToRemove = new List<string>();

            foreach (var aProject in Projects)
            {
                if (aProject.Project.ProjectReferences != null 
                    && aProject.Project.ProjectReferences.Count > 0)
                {
                    foreach (var aReference in aProject.Project.ProjectReferences)
                    {
                        SolutionProject referencedProject = null;

                        switch (aReference.Type)
                        {
                            case Enums.ProjectType.Classic:
                                {
                                    referencedProject = Projects.FirstOrDefault(x => x.ProjectId.Equals(aReference.Id, StringComparison.OrdinalIgnoreCase));
                                }
                                break;
                            case Enums.ProjectType.SDK:
                                {
                                    referencedProject = Projects.FirstOrDefault(x => x.ProjectName.Equals(aReference.Name, StringComparison.OrdinalIgnoreCase));
                                }
                                break;
                            default:
                                throw new Exception("Invalid project type");
                        }

                        if (referencedProject != null)
                        {
                            if (!string.IsNullOrWhiteSpace(referencedProject.Project.PackageId))
                            {
                                //check to see if a version overide has been provided
                                var altVersion = (nugetPackageOverrides != null && nugetPackageOverrides.ContainsKey(referencedProject.Project.PackageId))
                                    ? nugetPackageOverrides[referencedProject.Project.PackageId] : referencedProject.Project.PackageVersion;

                                aProject.AddPackageReference(referencedProject.Project.PackageId, altVersion);
                                aProject.RemoveProjectReference(referencedProject);

                                var exist = projectsToRemove.FirstOrDefault(x => x.Equals(referencedProject.ProjectName, StringComparison.OrdinalIgnoreCase));

                                if (exist == null)
                                    projectsToRemove.Add(referencedProject.ProjectName);
                            }
                            else
                            {
                                //fix the references to point locally instead on an SDK project
                                if (aProject.Project.Type == ProjectType.SDK)
                                {
                                    aProject.UpdateSdkProjectLocation(referencedProject, UpdatedPaths);
                                }
                            }
                        }
                    }
                }

            }

            foreach (var proj in projectsToRemove)
            {
                var aProj = Projects.FirstOrDefault(x => x.ProjectName.Equals(proj));

                if (aProj != null)
                {
                    var path = aProj.AbsolutePath;
                    var projId = aProj.ProjectId;

                    var parent = Path.GetDirectoryName(path);

                    if (Directory.Exists(parent))
                        Directory.Delete(parent, true);


                    var lines = File.ReadAllLines(_path);

                    var projLine = -1;
                    var endLing = -1;

                    var loopindex = 0;

                    var newLines = new List<string>();

                    foreach (var aLine in lines)
                    {
                        if (projLine == -1)
                        {
                            if (aLine.Contains(projId))
                                projLine = loopindex;
                            else
                                newLines.Add(aLine);
                        }
                        else
                        {
                           if (endLing == -1)
                            {
                                if (aLine.ToLower().Contains("endproject"))
                                {
                                    endLing = loopindex;
                                }
                                else if (aLine.ToLower().Contains("project"))
                                {
                                    endLing = loopindex;
                                }
                            }
                           else
                            {
                                newLines.Add(aLine);
                            }

                        }

                        loopindex++;
                    }

                    var its = newLines.Count;

                    File.WriteAllLines(_path, newLines.ToArray());
                }
            }
        }

        private void UpdateProjectsToBeRelative()
        {
            var lines = File.ReadAllLines(_path);

            var newLines = new List<string>();

            foreach (var aLine in lines)
            {
                if (aLine.ToLower().Contains("project("))
                {
                    var lne = aLine.Substring(aLine.IndexOf("=")).Replace("=", "").Trim();

                    var vals = lne.Split(',');

                    var projectName = vals[0].Replace("\"", "").Trim();

                    var csprojPath = vals[1].Replace("\"", "").Trim();

                    if (UpdatedPaths.ContainsKey(csprojPath))
                    {
                        var item = UpdatedPaths[csprojPath];

                        var newLine = aLine.Replace(csprojPath, item);

                        newLines.Add(newLine);
                    }
                    else
                    {
                        newLines.Add(aLine);
                    }
                    //if (!csprojPath.StartsWith(projectName))
                    //{
                    //    //do a thing with the lines
                    //    var line2 = csprojPath.IndexOf(projectName);

                    //    var sub = csprojPath.Substring(line2);

                    //    var newLine = aLine.Replace(csprojPath, sub);

                    //    newLines.Add(newLine);

                    //}
                    
                }
                else
                {
                    newLines.Add(aLine);
                }
                
 
            }

            File.WriteAllLines(_path, newLines.ToArray());
        }
    }


}
