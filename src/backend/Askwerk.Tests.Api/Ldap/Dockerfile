# SPDX-License-Identifier: AGPL-3.0-only
# A small OpenLDAP for the directory sign-in tests (AWK-12): Alpine's own packages, LDAPS only, a test CA made at
# build time, the people of data.ldif. Built by the full test run; never shipped.
FROM alpine:3.22
RUN apk add --no-cache openldap openldap-back-mdb openssl \
 && mkdir -p /certs /run/openldap /var/lib/openldap/data \
 && cd /certs \
 && openssl req -x509 -newkey rsa:2048 -nodes -keyout ca.key -out ca.pem -days 3650 -subj "/CN=Askwerk Test CA" \
 && openssl req -newkey rsa:2048 -nodes -keyout server.key -out server.csr -subj "/CN=localhost" \
 && printf "subjectAltName=DNS:localhost,IP:127.0.0.1\nbasicConstraints=CA:FALSE\nextendedKeyUsage=serverAuth\n" > ext.cnf \
 && openssl x509 -req -in server.csr -CA ca.pem -CAkey ca.key -CAcreateserial -out server.pem -days 3650 -extfile ext.cnf
COPY slapd.conf /etc/openldap/slapd.conf
COPY data.ldif /data.ldif
RUN slapadd -f /etc/openldap/slapd.conf -l /data.ldif
EXPOSE 636
CMD ["slapd", "-d", "0", "-h", "ldaps:///", "-f", "/etc/openldap/slapd.conf"]
