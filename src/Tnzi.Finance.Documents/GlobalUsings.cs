// System（System.IO 由渲染/字体文件各自的文件级 using 提供，避免与之重复）
global using System;
global using System.Collections.Generic;
global using System.Globalization;

// Microsoft
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.DependencyInjection.Extensions;
global using Microsoft.Extensions.Logging;

// Tnzi framework
global using Tnzi.Modules;
global using Tnzi.Results;
global using Tnzi.Utilities;

// Tnzi.Finance core（契约留核心 + Internal 经 InternalsVisibleTo 可见：MicrLineComposer）
global using Tnzi.Finance;
global using Tnzi.Finance.Metadata;
global using Tnzi.Finance.Services.Interfaces;
global using Tnzi.Finance.Services.Internal;

// Tnzi.Finance.Documents
global using Tnzi.Finance.Documents.Services;
