﻿using Microsoft.AspNetCore.Mvc;
using Grand.Framework.Mvc.ModelBinding;
using Grand.Framework.Mvc.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Grand.Framework.Mvc.Filters;
using Grand.Framework.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

using Grand.Services.Security;
using Grand.Framework.Security;
using Microsoft.AspNetCore.Http;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.Hosting;

namespace Grand.Web.Areas.Admin.Controllers
{
    [AdminAntiForgery(true)]
    public class RoxyFilemanController : BaseAdminController
    {
        #region Constants

        /// <summary>
        /// Default path to root directory of uploaded files (if appropriate settings are not specified)
        /// </summary>
        private const string DEFAULT_ROOT_DIRECTORY = "/images/uploaded";

        /// <summary>
        /// Path to directory of language files
        /// </summary>
        private const string LANGUAGE_DIRECTORY = "/lib/Roxy_Fileman/lang";

        /// <summary>
        /// Path to configuration file
        /// </summary>
        private const string CONFIGURATION_FILE = "/lib/Roxy_Fileman/conf.json";

        #endregion

        #region Fields

        private Dictionary<string, string> _settings;
        private Dictionary<string, string> _languageResources;

        private readonly IHostingEnvironment _hostingEnvironment;
        private readonly IPermissionService _permissionService;

        #endregion

        #region Ctor

        public RoxyFilemanController(IHostingEnvironment hostingEnvironment,
            IPermissionService permissionService)
        {
            this._hostingEnvironment = hostingEnvironment;
            this._permissionService = permissionService;
        }

        #endregion

        #region Methods

        public virtual void ProcessRequest()
        {
            //async requests are disabled in the js code, so use .Wait() method here
            ProcessRequestAsync().Wait();
        }

        #endregion

        #region Utitlies

        /// <summary>
        /// Process the incoming request
        /// </summary>
        /// <returns>A task that represents the completion of the operation</returns>
        protected virtual async Task ProcessRequestAsync()
        {
            var action = "DIRLIST";
            try
            {
                if (!_permissionService.Authorize(StandardPermissionProvider.HtmlEditorManagePictures))
                    throw new Exception("You don't have required permission");

                if (!StringValues.IsNullOrEmpty(this.HttpContext.Request.Query["a"]))
                    action = this.HttpContext.Request.Query["a"];

                switch (action.ToUpper())
                {
                    case "DIRLIST":
                        await GetDirectoriesAsync(this.HttpContext.Request.Query["type"]);
                        break;
                    case "FILESLIST":
                        await GetFilesAsync(this.HttpContext.Request.Query["d"], this.HttpContext.Request.Query["type"]);
                        break;
                    case "COPYDIR":
                        await CopyDirectoryAsync(this.HttpContext.Request.Query["d"], this.HttpContext.Request.Query["n"]);
                        break;
                    case "COPYFILE":
                        await CopyFileAsync(this.HttpContext.Request.Query["f"], this.HttpContext.Request.Query["n"]);
                        break;
                    case "CREATEDIR":
                        await CreateDirectoryAsync(this.HttpContext.Request.Query["d"], this.HttpContext.Request.Query["n"]);
                        break;
                    case "DELETEDIR":
                        await DeleteDirectoryAsync(this.HttpContext.Request.Query["d"]);
                        break;
                    case "DELETEFILE":
                        await DeleteFileAsync(this.HttpContext.Request.Query["f"]);
                        break;
                    case "DOWNLOAD":
                        await DownloadFileAsync(this.HttpContext.Request.Query["f"]);
                        break;
                    case "DOWNLOADDIR":
                        await DownloadDirectoryAsync(this.HttpContext.Request.Query["d"]);
                        break;
                    case "MOVEDIR":
                        await MoveDirectoryAsync(this.HttpContext.Request.Query["d"], this.HttpContext.Request.Query["n"]);
                        break;
                    case "MOVEFILE":
                        await MoveFileAsync(this.HttpContext.Request.Query["f"], this.HttpContext.Request.Query["n"]);
                        break;
                    case "RENAMEDIR":
                        await RenameDirectoryAsync(this.HttpContext.Request.Query["d"], this.HttpContext.Request.Query["n"]);
                        break;
                    case "RENAMEFILE":
                        await RenameFileAsync(this.HttpContext.Request.Query["f"], this.HttpContext.Request.Query["n"]);
                        break;
                    case "GENERATETHUMB":
                        int w = 140, h = 0;
                        int.TryParse(this.HttpContext.Request.Query["width"].ToString().Replace("px", ""), out w);
                        int.TryParse(this.HttpContext.Request.Query["height"].ToString().Replace("px", ""), out h);
                        CreateThumbnail(this.HttpContext.Request.Query["f"], w, h);
                        break;
                    case "UPLOAD":
                        await UploadFilesAsync(this.HttpContext.Request.Form["d"]);
                        break;
                    default:
                        await this.HttpContext.Response.WriteAsync(GetErrorResponse("This action is not implemented."));
                        break;
                }

            }
            catch (Exception ex)
            {
                if (action == "UPLOAD" && !IsAjaxRequest())
                    await this.HttpContext.Response.WriteAsync($"<script>parent.fileUploaded({GetErrorResponse(GetLanguageResource("E_UploadNoFiles"))});</script>");
                else
                    await this.HttpContext.Response.WriteAsync(GetErrorResponse(ex.Message));
            }
        }

        /// <summary>
        /// Get the virtual path to root directory of uploaded files 
        /// </summary>
        /// <returns>Path</returns>
        protected virtual string GetRootDirectory()
        {
            var filesRoot = GetSetting("FILES_ROOT");

            var sessionPathKey = GetSetting("SESSION_PATH_KEY");
            if (!string.IsNullOrEmpty(sessionPathKey))
                filesRoot = this.HttpContext.Session.GetString(sessionPathKey);

            if (string.IsNullOrEmpty(filesRoot))
                filesRoot = DEFAULT_ROOT_DIRECTORY;

            return filesRoot;
        }

        /// <summary>
        /// Get a virtual path with the root directory
        /// </summary>
        /// <param name="path">Path</param>
        /// <returns>Path</returns>
        protected virtual string GetVirtualPath(string path)
        {
            path = path ?? string.Empty;

            var rootDirectory = GetRootDirectory();
            if (!path.StartsWith(rootDirectory))
                path = rootDirectory + path;

            return path;
        }

        /// <summary>
        /// Get the absolute path by virtual path
        /// </summary>
        /// <param name="virtualPath">Virtual path</param>
        /// <returns>Path</returns>
        protected virtual string GetFullPath(string virtualPath)
        {
            virtualPath = virtualPath ?? string.Empty;
            if (!virtualPath.StartsWith("/"))
                virtualPath = "/" + virtualPath;
            virtualPath = virtualPath.TrimEnd('/');
            virtualPath = virtualPath.Replace('/', '\\');

            return _hostingEnvironment.WebRootPath + virtualPath;
        }

        /// <summary>
        /// Get a value of the configuration setting
        /// </summary>
        /// <param name="key">Setting key</param>
        /// <returns>Setting value</returns>
        protected virtual string GetSetting(string key)
        {
            if (_settings == null)
                _settings = ParseJson(GetFullPath(CONFIGURATION_FILE));

            if (_settings.TryGetValue(key, out string value))
                return value;

            return null;
        }

        /// <summary>
        /// Get the language resource value
        /// </summary>
        /// <param name="key">Language resource key</param>
        /// <returns>Language resource value</returns>
        protected virtual string GetLanguageResource(string key)
        {
            if (_languageResources == null)
                _languageResources = ParseJson(GetLanguageFile());

            if (_languageResources.TryGetValue(key, out string value))
                return value;

            return key;
        }

        /// <summary>
        /// Get the absolute path to the language resources file
        /// </summary>
        /// <returns>Path</returns>
        protected virtual string GetLanguageFile()
        {
            var languageCode = GetSetting("LANG");
            var languageFile = $"{LANGUAGE_DIRECTORY}/{languageCode}.json";

            if (!System.IO.File.Exists(GetFullPath(languageFile)))
                languageFile = $"{LANGUAGE_DIRECTORY}/en.json";

            return GetFullPath(languageFile);
        }

        /// <summary>
        /// Parse the JSON file
        /// </summary>
        /// <param name="file">Path to the file</param>
        /// <returns>Collection of keys and values from the parsed file</returns>
        protected virtual Dictionary<string, string> ParseJson(string file)
        {
            var result = new Dictionary<string, string>();
            var json = string.Empty;
            try
            {
                json = System.IO.File.ReadAllText(file, System.Text.Encoding.UTF8)?.Trim();
            }
            catch { }

            if (string.IsNullOrEmpty(json))
                return result;

            if (json.StartsWith("{"))
                json = json.Substring(1, json.Length - 2);

            json = json.Trim();
            json = json.Substring(1, json.Length - 2);

            var lines = Regex.Split(json, "\"\\s*,\\s*\"");
            foreach (var line in lines)
            {
                var tmp = Regex.Split(line, "\"\\s*:\\s*\"");
                try
                {
                    if (!string.IsNullOrEmpty(tmp[0]) && !result.ContainsKey(tmp[0]))
                        result.Add(tmp[0], tmp[1]);
                }
                catch { }
            }

            return result;
        }

        /// <summary>
        /// Get a file type by file extension
        /// </summary>
        /// <param name="fileExtension">File extension</param>
        /// <returns>File type</returns>
        protected virtual string GetFileType(string fileExtension)
        {
            var fileType = "file";

            fileExtension = fileExtension.ToLower();
            if (fileExtension == ".jpg" || fileExtension == ".jpeg" || fileExtension == ".png" || fileExtension == ".gif")
                fileType = "image";

            if (fileExtension == ".swf" || fileExtension == ".flv")
                fileType = "flash";

            return fileType;
        }

        /// <summary>
        /// Check whether there are any restrictions on handling the file
        /// </summary>
        /// <param name="path">Path to the file</param>
        /// <returns>True if the file can be handled; otherwise false</returns>
        protected virtual bool CanHandleFile(string path)
        {
            var result = false;
            var fileExtension = new FileInfo(path).Extension.Replace(".", "").ToLower();

            var forbiddenUploads = GetSetting("FORBIDDEN_UPLOADS").Trim().ToLower();
            if (!string.IsNullOrEmpty(forbiddenUploads))
            {
                var forbiddenFileExtensions = new ArrayList(Regex.Split(forbiddenUploads, "\\s+"));
                result = !forbiddenFileExtensions.Contains(fileExtension);
            }

            var allowedUploads = GetSetting("ALLOWED_UPLOADS").Trim().ToLower();
            if (!string.IsNullOrEmpty(allowedUploads))
            {
                var allowedFileExtensions = new ArrayList(Regex.Split(allowedUploads, "\\s+"));
                result = allowedFileExtensions.Contains(fileExtension);
            }

            return result;
        }

        /// <summary>
        /// Get the string to write to the response
        /// </summary>
        /// <param name="type">Type of the response</param>
        /// <param name="message">Additional message</param>
        /// <returns>String to write to the response</returns>
        protected virtual string GetResponse(string type, string message)
        {
            return $"{{\"res\":\"{type}\",\"msg\":\"{message?.Replace("\"", "\\\"")}\"}}";
        }

        /// <summary>
        /// Get the string to write a success response
        /// </summary>
        /// <param name="message">Additional message</param>
        /// <returns>String to write to the response</returns>
        protected virtual string GetSuccessResponse(string message = null)
        {
            return GetResponse("ok", message);
        }

        /// <summary>
        /// Get the string to write an error response
        /// </summary>
        /// <param name="message">Additional message</param>
        /// <returns>String to write to the response</returns>
        protected virtual string GetErrorResponse(string message = null)
        {
            return GetResponse("error", message);
        }

        /// <summary>
        /// Get all available directories as a directory tree
        /// </summary>
        /// <param name="type">Type of the file</param>
        /// <returns>A task that represents the completion of the operation</returns>
        protected virtual async Task GetDirectoriesAsync(string type)
        {
            var rootDirectoryPath = GetFullPath(GetVirtualPath(null));
            var rootDirectory = new DirectoryInfo(rootDirectoryPath);
            if (!rootDirectory.Exists)
                throw new Exception("Invalid files root directory. Check your configuration.");

            var allDirectories = GetDirectories(rootDirectory.FullName);
            allDirectories.Insert(0, rootDirectory.FullName);

            var localPath = GetFullPath(null);
            await this.HttpContext.Response.WriteAsync("[");
            for (var i = 0; i < allDirectories.Count; i++)
            {
                var directoryPath = (string)allDirectories[i];
                await this.HttpContext.Response.WriteAsync($"{{\"p\":\"/{directoryPath.Replace(localPath, string.Empty).Replace("\\", "/").TrimStart('/')}\",\"f\":\"{GetFiles(directoryPath, type).Count}\",\"d\":\"{Directory.GetDirectories(directoryPath).Length}\"}}");
                if (i < allDirectories.Count - 1)
                    await this.HttpContext.Response.WriteAsync(",");
            }
            await this.HttpContext.Response.WriteAsync("]");
        }

        /// <summary>
        /// Get directories in the passed parent directory
        /// </summary>
        /// <param name="parentDirectoryPath">Path to the parent directory</param>
        /// <returns>Array of the paths to the directories</returns>
        protected virtual ArrayList GetDirectories(string parentDirectoryPath)
        {
            var directories = new ArrayList();

            var directoryNames = Directory.GetDirectories(parentDirectoryPath);
            foreach (var directory in directoryNames)
            {
                directories.Add(directory);
                directories.AddRange(GetDirectories(directory));
            }

            return directories;
        }

        /// <summary>
        /// Get files in the passed directory
        /// </summary>
        /// <param name="directoryPath">Path to the files directory</param>
        /// <param name="type">Type of the files</param>
        /// <returns>A task that represents the completion of the operation</returns>
        protected virtual async Task GetFilesAsync(string directoryPath, string type)
        {
            directoryPath = GetVirtualPath(directoryPath);
            var files = GetFiles(GetFullPath(directoryPath), type);

            await this.HttpContext.Response.WriteAsync("[");
            for (var i = 0; i < files.Count; i++)
            {
                var width = 0;
                var height = 0;
                var file = new FileInfo(files[i]);
                if (GetFileType(file.Extension) == "image")
                {
                    try
                    {
                        using (var stream = new FileStream(file.FullName, FileMode.Open))
                        {
                            using (var image = Image.FromStream(stream))
                            {
                                width = image.Width;
                                height = image.Height;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }
                }
                await this.HttpContext.Response.WriteAsync($"{{\"p\":\"{directoryPath.TrimEnd('/')}/{file.Name}\",\"t\":\"{Math.Ceiling(GetTimestamp(file.LastWriteTime))}\",\"s\":\"{file.Length}\",\"w\":\"{width}\",\"h\":\"{height}\"}}");

                if (i < files.Count - 1)
                    await this.HttpContext.Response.WriteAsync(",");
            }
            await this.HttpContext.Response.WriteAsync("]");
        }

        /// <summary>
        /// Get files in the passed directory
        /// </summary>
        /// <param name="directoryPath">Path to the files directory</param>
        /// <param name="type">Type of the files</param>
        /// <returns>List of paths to the files</returns>
        protected virtual List<string> GetFiles(string directoryPath, string type)
        {
            if (type == "#")
                type = string.Empty;

            var files = new List<string>();
            foreach (var fileName in Directory.GetFiles(directoryPath))
            {
                if (string.IsNullOrEmpty(type) || GetFileType(new FileInfo(fileName).Extension) == type)
                    files.Add(fileName);
            }

            return files;
        }

        /// <summary>
        /// Get the Unix timestamp by passed date
        /// </summary>
        /// <param name="date">Date and time</param>
        /// <returns>Unix timestamp</returns>
        protected virtual double GetTimestamp(DateTime date)
        {
            return (date.ToLocalTime() - new DateTime(1970, 1, 1, 0, 0, 0).ToLocalTime()).TotalSeconds;
        }

        /// <summary>
        /// Copy the directory
        /// </summary>
        /// <param name="sourcePath">Path to the source directory</param>
        /// <param name="destinationPath">Path to the destination directory</param>
        /// <returns>A task that represents the completion of the operation</returns>
        protected virtual async Task CopyDirectoryAsync(string sourcePath, string destinationPath)
        {
            var directoryPath = GetFullPath(GetVirtualPath(sourcePath));
            var directory = new DirectoryInfo(directoryPath);
            if (!directory.Exists)
                throw new Exception(GetLanguageResource("E_CopyDirInvalidPath"));

            var newDirectoryPath = GetFullPath(GetVirtualPath($"{destinationPath.TrimEnd('/')}/{directory.Name}"));
            var newDirectory = new DirectoryInfo(newDirectoryPath);
            if (newDirectory.Exists)
                throw new Exception(GetLanguageResource("E_DirAlreadyExists"));

            CopyDirectory(directory.FullName, newDirectory.FullName);

            await this.HttpContext.Response.WriteAsync(GetSuccessResponse());
        }

        /// <summary>
        /// Сopy the directory with the embedded files and directories
        /// </summary>
        /// <param name="sourcePath">Path to the source directory</param>
        /// <param name="destinationPath">Path to the destination directory</param>
        protected virtual void CopyDirectory(string sourcePath, string destinationPath)
        {
            var existingFiles = Directory.GetFiles(sourcePath);
            var existingDirectories = Directory.GetDirectories(sourcePath);

            if (!Directory.Exists(destinationPath))
                Directory.CreateDirectory(destinationPath);

            foreach (var file in existingFiles)
            {
                var filePath = Path.Combine(destinationPath, new FileInfo(file).Name);
                if (!System.IO.File.Exists(filePath))
                    System.IO.File.Copy(file, filePath);
            }

            foreach (var directory in existingDirectories)
            {
                var directoryPath = Path.Combine(destinationPath, new DirectoryInfo(directory).Name);
                CopyDirectory(directory, directoryPath);
            }
        }

        /// <summary>
        /// Copy the file
        /// </summary>
        /// <param name="sourcePath">Path to the source file</param>
        /// <param name="destinationPath">Path to the destination file</param>
        /// <returns>A task that represents the completion of the operation</returns>
        protected virtual async Task CopyFileAsync(string sourcePath, string destinationPath)
        {
            var filePath = GetFullPath(GetVirtualPath(sourcePath));
            var file = new FileInfo(filePath);
            if (!file.Exists)
                throw new Exception(GetLanguageResource("E_CopyFileInvalisPath"));

            destinationPath = GetFullPath(GetVirtualPath(destinationPath));
            var newFileName = GetUniqueFileName(destinationPath, file.Name);
            try
            {
                System.IO.File.Copy(file.FullName, Path.Combine(destinationPath, newFileName));
                await this.HttpContext.Response.WriteAsync(GetSuccessResponse());
            }
            catch
            {
                throw new Exception(GetLanguageResource("E_CopyFile"));
            }
        }

        /// <summary>
        /// Get the unique name of the file (add -copy-(N) to the file name if there is already a file with that name in the directory)
        /// </summary>
        /// <param name="directoryPath">Path to the file directory</param>
        /// <param name="fileName">Original file name</param>
        /// <returns>Unique name of the file</returns>
        protected virtual string GetUniqueFileName(string directoryPath, string fileName)
        {
            var uniqueFileName = fileName;

            int i = 0;
            while (System.IO.File.Exists(Path.Combine(directoryPath, uniqueFileName)))
            {
                uniqueFileName = $"{Path.GetFileNameWithoutExtension(fileName)}-Copy-{++i}{Path.GetExtension(fileName)}";
            }

            return uniqueFileName;
        }

        /// <summary>
        /// Create the new directory
        /// </summary>
        /// <param name="parentDirectoryPath">Path to the parent directory</param>
        /// <param name="name">Name of the new directory</param>
        /// <returns>A task that represents the completion of the operation</returns>
        protected virtual async Task CreateDirectoryAsync(string parentDirectoryPath, string name)
        {
            parentDirectoryPath = GetFullPath(GetVirtualPath(parentDirectoryPath));
            if (!Directory.Exists(parentDirectoryPath))
                throw new Exception(GetLanguageResource("E_CreateDirInvalidPath"));

            try
            {
                var path = Path.Combine(parentDirectoryPath, name);
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                await this.HttpContext.Response.WriteAsync(GetSuccessResponse());
            }
            catch
            {
                throw new Exception(GetLanguageResource("E_CreateDirFailed"));
            }
        }

        /// <summary>
        /// Delete the directory
        /// </summary>
        /// <param name="path">Path to the directory</param>
        /// <returns>A task that represents the completion of the operation</returns>
        protected virtual async Task DeleteDirectoryAsync(string path)
        {
            path = GetVirtualPath(path);
            if (path == GetRootDirectory())
                throw new Exception(GetLanguageResource("E_CannotDeleteRoot"));

            path = GetFullPath(path);
            if (!Directory.Exists(path))
                throw new Exception(GetLanguageResource("E_DeleteDirInvalidPath"));

            if (Directory.GetDirectories(path).Length > 0 || Directory.GetFiles(path).Length > 0)
                throw new Exception(GetLanguageResource("E_DeleteNonEmpty"));

            try
            {
                Directory.Delete(path);
                await this.HttpContext.Response.WriteAsync(GetSuccessResponse());
            }
            catch
            {
                throw new Exception(GetLanguageResource("E_CannotDeleteDir"));
            }
        }

        /// <summary>
        /// Delete the file
        /// </summary>
        /// <param name="path">Path to the file</param>
        /// <returns>A task that represents the completion of the operation</returns>
        protected virtual async Task DeleteFileAsync(string path)
        {
            path = GetFullPath(GetVirtualPath(path));
            if (!System.IO.File.Exists(path))
                throw new Exception(GetLanguageResource("E_DeleteFileInvalidPath"));

            try
            {
                System.IO.File.Delete(path);
                await this.HttpContext.Response.WriteAsync(GetSuccessResponse());
            }
            catch
            {
                throw new Exception(GetLanguageResource("E_DeletеFile"));
            }
        }

        /// <summary>
        /// Download the directory from the server as a zip archive
        /// </summary>
        /// <param name="path">Path to the directory</param>
        /// <returns>A task that represents the completion of the operation</returns>
        public async Task DownloadDirectoryAsync(string path)
        {
            path = GetVirtualPath(path).TrimEnd('/');
            var fullPath = GetFullPath(path);
            if (!Directory.Exists(fullPath))
                throw new Exception(GetLanguageResource("E_CreateArchive"));

            var zipName = new FileInfo(fullPath).Name + ".zip";
            var zipPath = $"/{zipName}";
            if (path != GetRootDirectory())
                zipPath = GetVirtualPath(zipPath);
            zipPath = GetFullPath(zipPath);

            if (System.IO.File.Exists(zipPath))
                System.IO.File.Delete(zipPath);

            ZipFile.CreateFromDirectory(fullPath, zipPath, CompressionLevel.Fastest, true);

            this.HttpContext.Response.Clear();
            this.HttpContext.Response.Headers.Add("Content-Disposition", $"attachment; filename=\"{zipName}\"");
            this.HttpContext.Response.ContentType = "application/force-download";
            await this.HttpContext.Response.SendFileAsync(zipPath);

            System.IO.File.Delete(zipPath);
        }

        /// <summary>
        /// Download the file from the server
        /// </summary>
        /// <param name="path">Path to the file</param>
        /// <returns>A task that represents the completion of the operation</returns>
        protected virtual async Task DownloadFileAsync(string path)
        {
            var filePath = GetFullPath(GetVirtualPath(path));
            var file = new FileInfo(filePath);
            if (file.Exists)
            {
                this.HttpContext.Response.Clear();
                this.HttpContext.Response.Headers.Add("Content-Disposition", $"attachment; filename=\"{file.Name}\"");
                this.HttpContext.Response.ContentType = "application/force-download";
                await this.HttpContext.Response.SendFileAsync(file.FullName);
            }
        }

        /// <summary>
        /// Move the directory
        /// </summary>
        /// <param name="sourcePath">Path to the source directory</param>
        /// <param name="destinationPath">Path to the destination directory</param>
        /// <returns>A task that represents the completion of the operation</returns>
        protected virtual async Task MoveDirectoryAsync(string sourcePath, string destinationPath)
        {
            var fullSourcePath = GetFullPath(GetVirtualPath(sourcePath));
            var sourceDirectory = new DirectoryInfo(fullSourcePath);
            destinationPath = GetFullPath(GetVirtualPath(Path.Combine(destinationPath, sourceDirectory.Name)));
            var destinationDirectory = new DirectoryInfo(destinationPath);
            if (destinationDirectory.FullName.IndexOf(sourceDirectory.FullName) == 0)
                throw new Exception(GetLanguageResource("E_CannotMoveDirToChild"));

            if (!sourceDirectory.Exists)
                throw new Exception(GetLanguageResource("E_MoveDirInvalisPath"));

            if (destinationDirectory.Exists)
                throw new Exception(GetLanguageResource("E_DirAlreadyExists"));

            try
            {
                sourceDirectory.MoveTo(destinationDirectory.FullName);
                await this.HttpContext.Response.WriteAsync(GetSuccessResponse());
            }
            catch
            {
                throw new Exception($"{GetLanguageResource("E_MoveDir")} \"{sourcePath}\"");
            }
        }

        /// <summary>
        /// Move the file
        /// </summary>
        /// <param name="sourcePath">Path to the source file</param>
        /// <param name="destinationPath">Path to the destination file</param>
        /// <returns>A task that represents the completion of the operation</returns>
        protected virtual async Task MoveFileAsync(string sourcePath, string destinationPath)
        {
            var fullSourcePath = GetFullPath(GetVirtualPath(sourcePath));
            var sourceFile = new FileInfo(fullSourcePath);
            if (!sourceFile.Exists)
                throw new Exception(GetLanguageResource("E_MoveFileInvalisPath"));

            destinationPath = GetFullPath(GetVirtualPath(destinationPath));
            var destinationFile = new FileInfo(destinationPath);
            if (destinationFile.Exists)
                throw new Exception(GetLanguageResource("E_MoveFileAlreadyExists"));

            if (!CanHandleFile(destinationFile.Name))
                throw new Exception(GetLanguageResource("E_FileExtensionForbidden"));

            try
            {
                sourceFile.MoveTo(destinationFile.FullName);
                await this.HttpContext.Response.WriteAsync(GetSuccessResponse());
            }
            catch
            {
                throw new Exception($"{GetLanguageResource("E_MoveFile")} \"{sourcePath}\"");
            }
        }

        /// <summary>
        /// Rename the directory
        /// </summary>
        /// <param name="sourcePath">Path to the source directory</param>
        /// <param name="newName">New name of the directory</param>
        /// <returns>A task that represents the completion of the operation</returns>
        protected virtual async Task RenameDirectoryAsync(string sourcePath, string newName)
        {
            var fullSourcePath = GetFullPath(GetVirtualPath(sourcePath));
            var sourceDirectory = new DirectoryInfo(fullSourcePath);
            var destinationDirectory = new DirectoryInfo(Path.Combine(sourceDirectory.Parent.FullName, newName));
            if (GetVirtualPath(sourcePath) == GetRootDirectory())
                throw new Exception(GetLanguageResource("E_CannotRenameRoot"));

            if (!sourceDirectory.Exists)
                throw new Exception(GetLanguageResource("E_RenameDirInvalidPath"));

            if (destinationDirectory.Exists)
                throw new Exception(GetLanguageResource("E_DirAlreadyExists"));

            try
            {
                sourceDirectory.MoveTo(destinationDirectory.FullName);
                await this.HttpContext.Response.WriteAsync(GetSuccessResponse());
            }
            catch
            {
                throw new Exception($"{GetLanguageResource("E_RenameDir")} \"{sourcePath}\"");
            }
        }

        /// <summary>
        /// Rename the file
        /// </summary>
        /// <param name="sourcePath">Path to the source file</param>
        /// <param name="newName">New name of the file</param>
        /// <returns>A task that represents the completion of the operation</returns>
        protected virtual async Task RenameFileAsync(string sourcePath, string newName)
        {
            var fullSourcePath = GetFullPath(GetVirtualPath(sourcePath));
            var sourceFile = new FileInfo(fullSourcePath);
            if (!sourceFile.Exists)
                throw new Exception(GetLanguageResource("E_RenameFileInvalidPath"));

            if (!CanHandleFile(newName))
                throw new Exception(GetLanguageResource("E_FileExtensionForbidden"));

            try
            {
                var destinationPath = Path.Combine(sourceFile.Directory.FullName, newName);
                var destinationFile = new FileInfo(destinationPath);
                sourceFile.MoveTo(destinationFile.FullName);
                await this.HttpContext.Response.WriteAsync(GetSuccessResponse());
            }
            catch
            {
                throw new Exception($"{GetLanguageResource("E_RenameFile")} \"{sourcePath}\"");
            }
        }

        /// <summary>
        /// Create the thumbnail of the image and write it to the response
        /// </summary>
        /// <param name="path">Path to the image</param>
        /// <param name="width">Width</param>
        /// <param name="height">Height</param>
        protected virtual void CreateThumbnail(string path, int width, int height)
        {
            path = GetFullPath(GetVirtualPath(path));
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                using (var image = new Bitmap(Bitmap.FromStream(stream)))
                {
                    var cropWidth = image.Width;
                    var cropHeight = image.Height;
                    var cropX = 0;
                    var cropY = 0;

                    var imgRatio = (double)image.Width / (double)image.Height;

                    if (height == 0)
                        height = Convert.ToInt32(Math.Floor((double)width / imgRatio));

                    if (width > image.Width)
                        width = image.Width;
                    if (height > image.Height)
                        height = image.Height;

                    var cropRatio = (double)width / (double)height;
                    cropWidth = Convert.ToInt32(Math.Floor((double)image.Height * cropRatio));
                    cropHeight = Convert.ToInt32(Math.Floor((double)cropWidth / cropRatio));

                    if (cropWidth > image.Width)
                    {
                        cropWidth = image.Width;
                        cropHeight = Convert.ToInt32(Math.Floor((double)cropWidth / cropRatio));
                    }

                    if (cropHeight > image.Height)
                    {
                        cropHeight = image.Height;
                        cropWidth = Convert.ToInt32(Math.Floor((double)cropHeight * cropRatio));
                    }

                    if (cropWidth < image.Width)
                        cropX = Convert.ToInt32(Math.Floor((double)(image.Width - cropWidth) / 2));
                    if (cropHeight < image.Height)
                        cropY = Convert.ToInt32(Math.Floor((double)(image.Height - cropHeight) / 2));

                    using (var cropImg = image.Clone(new Rectangle(cropX, cropY, cropWidth, cropHeight), PixelFormat.DontCare))
                    {
                        this.HttpContext.Response.Headers.Add("Content-Type", "image/png");
                        cropImg.GetThumbnailImage(width, height, () => { return false; }, IntPtr.Zero).Save(this.HttpContext.Response.Body, ImageFormat.Png);
                        this.HttpContext.Response.Body.Close();
                    }
                }
            }
        }

        /// <summary>
        /// Get the file format of the image
        /// </summary>
        /// <param name="path">Path to the image</param>
        /// <returns>Image format</returns>
        protected virtual ImageFormat GetImageFormat(string path)
        {
            var fileExtension = new FileInfo(path).Extension.ToLower();
            switch (fileExtension)
            {
                case ".png":
                    return ImageFormat.Png;
                case ".gif":
                    return ImageFormat.Gif;
                default:
                    return ImageFormat.Jpeg;
            }
        }

        /// <summary>
        /// Resize the image
        /// </summary>
        /// <param name="sourcePath">Path to the source image</param>
        /// <param name="destinstionPath">Path to the destination image</param>
        /// <param name="width">Width</param>
        /// <param name="height">Height</param>
        protected virtual void ImageResize(string sourcePath, string destinstionPath, int width, int height)
        {
            using (var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read))
            {
                using (var image = Image.FromStream(stream))
                {
                    var ratio = (float)image.Width / (float)image.Height;
                    if ((image.Width <= width && image.Height <= height) || (width == 0 && height == 0))
                        return;

                    var newWidth = width;
                    int newHeight = Convert.ToInt16(Math.Floor((float)newWidth / ratio));
                    if ((height > 0 && newHeight > height) || (width == 0))
                    {
                        newHeight = height;
                        newWidth = Convert.ToInt16(Math.Floor((float)newHeight * ratio));
                    }

                    using (var newImage = new Bitmap(newWidth, newHeight))
                    {
                        using (var graphics = Graphics.FromImage(newImage))
                        {
                            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            graphics.DrawImage(image, 0, 0, newWidth, newHeight);
                            if (!string.IsNullOrEmpty(destinstionPath))
                                newImage.Save(destinstionPath, GetImageFormat(destinstionPath));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Whether the request is made with ajax 
        /// </summary>
        /// <returns>True or false</returns>
        protected virtual bool IsAjaxRequest()
        {
            return this.HttpContext.Request.Form != null &&
                !StringValues.IsNullOrEmpty(this.HttpContext.Request.Form["method"]) &&
                this.HttpContext.Request.Form["method"] == "ajax";
        }

        /// <summary>
        /// Upload files to a directory on passed path
        /// </summary>
        /// <param name="directoryPath">Path to directory to upload files</param>
        /// <returns>A task that represents the completion of the operation</returns>
        protected virtual async Task UploadFilesAsync(string directoryPath)
        {
            var result = GetSuccessResponse();
            var hasErrors = false;
            try
            {
                directoryPath = GetFullPath(GetVirtualPath(directoryPath));
                for (var i = 0; i < this.HttpContext.Request.Form.Files.Count; i++)
                {
                    var fileName = this.HttpContext.Request.Form.Files[i].FileName;
                    if (CanHandleFile(fileName))
                    {
                        var file = new FileInfo(fileName);
                        var uniqueFileName = GetUniqueFileName(directoryPath, file.Name);
                        var destinationFile = Path.Combine(directoryPath, uniqueFileName);
                        using (var stream = new FileStream(destinationFile, FileMode.OpenOrCreate))
                        {
                            this.HttpContext.Request.Form.Files[i].CopyTo(stream);
                        }
                        if (GetFileType(new FileInfo(uniqueFileName).Extension) == "image")
                        {
                            int.TryParse(GetSetting("MAX_IMAGE_WIDTH"), out int w);
                            int.TryParse(GetSetting("MAX_IMAGE_HEIGHT"), out int h);
                            ImageResize(destinationFile, destinationFile, w, h);
                        }
                    }
                    else
                    {
                        hasErrors = true;
                        result = GetErrorResponse(GetLanguageResource("E_UploadNotAll"));
                    }
                }
            }
            catch (Exception ex)
            {
                result = GetErrorResponse(ex.Message);
            }
            if (IsAjaxRequest())
            {
                if (hasErrors)
                    result = GetErrorResponse(GetLanguageResource("E_UploadNotAll"));

                await this.HttpContext.Response.WriteAsync(result);
            }
            else
                await this.HttpContext.Response.WriteAsync($"<script>parent.fileUploaded({result});</script>");
        }

        #endregion
    }
}