---
name: Bug report
about: Something behaves differently from what the docs or the API says
title: ''
labels: bug
assignees: ''
---

## What happened

<!-- The actual behaviour. Include the exception type and message if one was thrown. -->

## What you expected

<!-- And where that expectation comes from: docs, XML comments, an API signature. -->

## Reproducing it

<!--
  Smallest setup that shows the problem. The module list matters more than you'd think,
  since several behaviours change depending on which modules are loaded.
-->

```csharp
[DependsOn(/* which modules were loaded */)]
public class StartupModule : HostingModule { }
```

Steps:
1.
2.

## Environment

- Tnzi.NET version:
- .NET SDK:
- Database provider: <!-- SQL Server / PostgreSQL / MySQL / SQLite -->
- Multi-tenancy: <!-- on / off; several guards behave differently -->

## Anything you already ruled out

<!-- Optional, but it saves a round trip. -->
