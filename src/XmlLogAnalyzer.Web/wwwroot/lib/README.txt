VENDORED CLIENT LIBRARIES
=========================

The application expects the following two folders to exist beside this file:

  bootstrap/
    bootstrap.min.css
    bootstrap.bundle.min.js

  bootstrap-icons/
    bootstrap-icons.min.css
    fonts/bootstrap-icons.woff
    fonts/bootstrap-icons.woff2

How to populate them (one-time, no npm required)
------------------------------------------------

PowerShell (Windows):

    cd src\XmlLogAnalyzer.Web\wwwroot\lib

    # Bootstrap 5.3 - CSS + bundled JS (includes Popper)
    mkdir bootstrap
    iwr https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css       -OutFile bootstrap\bootstrap.min.css
    iwr https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js  -OutFile bootstrap\bootstrap.bundle.min.js

    # Bootstrap Icons 1.11
    mkdir bootstrap-icons\fonts
    iwr https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css       -OutFile bootstrap-icons\bootstrap-icons.min.css
    iwr https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/fonts/bootstrap-icons.woff    -OutFile bootstrap-icons\fonts\bootstrap-icons.woff
    iwr https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/fonts/bootstrap-icons.woff2   -OutFile bootstrap-icons\fonts\bootstrap-icons.woff2

curl (cross-platform):

    curl -L -o bootstrap/bootstrap.min.css            https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css
    curl -L -o bootstrap/bootstrap.bundle.min.js      https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js
    curl -L -o bootstrap-icons/bootstrap-icons.min.css https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css

These are static files only -- no npm or node required. They are committed once
and distributed with the build output.
