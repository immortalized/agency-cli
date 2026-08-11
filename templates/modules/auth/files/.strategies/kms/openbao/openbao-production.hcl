ui = false

storage "raft" {
  path    = "/openbao/file"
  node_id = "__PROJECT_SLUG__-production"
}

listener "tcp" {
  address     = "0.0.0.0:8200"
  tls_disable = true
}

seal "awskms" {
}

api_addr     = "http://openbao:8200"
cluster_addr = "http://openbao:8201"
