# Package Symlinker [![Github license](https://img.shields.io/github/license/codewriter-packages/Package-Symlinker.svg?style=flat-square)](#) [![GitHub package.json version](https://img.shields.io/github/package-json/v/codewriter-packages/Package-Symlinker?style=flat-square)](#)
Tool for maintaining symbolic linked packages for Unity

[![Package Symlinker Preview](https://user-images.githubusercontent.com/26966368/104042200-3a2b1b00-51eb-11eb-875c-9503cf2af12b.png)](#)

Supported Editor Platforms:
* Windows
* MacOS
* Linux*

> Linux should work well, but has not been tested.

## About

Unity allow you to develop modular code and reuse it across multiple projects using packages.
This packages usually placed in separate Unity project.
So you development process may be similar to the following:
1) Switch to project with your packages
2) Edit the code
3) Publish new package version to registry (github, bitbucket or your own)
4) Switch to real project
5) Update package to latest version
6) Check that in real project all work correctly
7) Repeat from step 1 if any bug found

It seems that it would be much more easy to edit packages directly in a real project?

**Package Symlinker is a tool that allows you in a couple of clicks create symbolic links to packages, so you can edit any package as if it were located right in the project.** 

With this tool your workflow can be simplified to:
1) Create a symbolic link to the package
2) Edit the package directly in real project
3) Delete symbolic link
3) Publish new package version to registry
5) Update real project to latest package version

## Install
 Library distributed as git package ([How to install package from git URL](https://docs.unity3d.com/Manual/upm-ui-giturl.html))
 <br>Git URL: `https://github.com/codewriter-packages/Package-Symlinker.git`
 
## License

Package Symlinker is [MIT licensed](./LICENSE.md).
