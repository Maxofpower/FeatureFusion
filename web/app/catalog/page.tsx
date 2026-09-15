import { fetcher } from "@/lib/fetcher"
import type { Catalog } from "@/models"
import { CatalogPage } from "./_components/catalog"

export default async function Catalog() {

    const getCatalogs = await fetcher<Catalog>("/catalog/products?page=1&pageSize=24")
    console.log(getCatalogs)

    if(!getCatalogs.success || getCatalogs.data === undefined) {
        return <div>Error: {getCatalogs.message}</div>
    }

    return <CatalogPage catalogs={getCatalogs.data} />
}