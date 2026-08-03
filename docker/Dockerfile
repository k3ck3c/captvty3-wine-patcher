FROM wine-lab-runtime:dotnet48-gui-comctl32-wine10.1-r1

COPY entrypoint.sh /usr/local/bin/captvty3-entrypoint

RUN chmod +x /usr/local/bin/captvty3-entrypoint

ENTRYPOINT ["/usr/local/bin/captvty3-entrypoint"]
