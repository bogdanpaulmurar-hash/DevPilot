@echo off
rem Installs the pre-commit secret check into this clone.
copy /Y "%~dp0pre-commit" "%~dp0..\.git\hooks\pre-commit" >nul && echo Pre-commit hook installed.
