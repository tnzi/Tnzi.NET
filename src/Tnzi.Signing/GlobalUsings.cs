// System（System / System.Collections.Generic / System.Linq / System.Threading[.Tasks]
// 由 ImplicitUsings 提供，不在此重复）
global using System.Globalization;
global using System.Linq.Expressions;
global using System.Text;
global using System.Text.RegularExpressions;

// Microsoft
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore.Metadata.Builders;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.DependencyInjection.Extensions;
global using Microsoft.Extensions.Logging;

// Tnzi framework
global using Tnzi.Application;
global using Tnzi.AspNetCore.Extensions;
global using Tnzi.AspNetCore.Models;
global using Tnzi.AspNetCore.Mvc;
global using Tnzi.Data;
global using Tnzi.Domain.Entities;
global using Tnzi.Domain.Repositories;
global using Tnzi.EFCore;
global using Tnzi.EFCore.Extensions;
global using Tnzi.EFCore.Internal;
global using Tnzi.Mapster;
global using Tnzi.Json;
global using Tnzi.Modules;
global using Tnzi.Results;
global using Tnzi.Security.Authorization;
// Tnzi.Storage：根命名空间是为了 StorageModule（[DependsOn] 要引用它），
// .Services 是为了 IFileStorageService。两者缺一不可 —— 根下的类型不会因为
// 导入了子命名空间就可解析。
global using Tnzi.Storage;
global using Tnzi.Storage.Services;
global using Tnzi.Utilities;

// Tnzi.Documents（PDF 原语）
// 本模块曾名 Tnzi.Documents.Signing，那时 DocumentsModule 是靠命名空间父链隐式解析的；
// 改名后父链不再经过 Tnzi.Documents，故必须显式导入。
global using Tnzi.Documents;
global using Tnzi.Documents.Models;
global using Tnzi.Documents.Services;

// Tnzi.Signing
global using Tnzi.Signing.Dtos;
global using Tnzi.Signing.Entities;
global using Tnzi.Signing.Metadata;
global using Tnzi.Signing.Permissions;
global using Tnzi.Signing.Services;
global using Tnzi.Signing.Services.Internal;
