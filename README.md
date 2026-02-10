# Nuget Package Downloader

This is a command line tool to download specified nuget packages, and their dependencies. The whole dependency tree will be downloaded, including direct nodes and indirect nodes. It's meant to be useful for offline developing environments.

Dependency versions are resolved according to the algorithm described by [officail documents](https://learn.microsoft.com/en-us/nuget/concepts/dependency-resolution).

## Usage
```
Description:                                                                                                                                                                                                               
  Download nuget packages and dependencies

Usage:
  nd <packages>... [options]

Arguments:
  <packages>  One or more package id with optional version. example: Newtonsoft.Json:13.0.1 or Autofac

Options:
  -o, --output <output>  Saving folder path, defaults to working directory
  -f, --force            Download and update existing packages
  --dry-run              Display what will happen without actually downloading
  -?, -h, --help         Show help and usage information
  --version              Show version information
```
