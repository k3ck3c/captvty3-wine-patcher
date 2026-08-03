# syntax=docker/dockerfile:1

ARG WINE_BASE=wine-lab-runtime:dotnet48-wine10.1-r1
FROM ${WINE_BASE}

USER lab

RUN winetricks -q corefonts gdiplus \
    && wineserver -w

CMD ["bash"]