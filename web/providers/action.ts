'use server'

import { API_ROUTES } from "@/lib/constants/routes"
import { fetcher } from "@/lib/fetcher"
import { ProductDetail } from "@/models"

export const getProductName = async (query: string) => {
    try {
        const getProduct = await fetcher<ProductDetail>(`${API_ROUTES.CATALOG}?${encodeURIComponent(query)}`)

        if (!getProduct.success || getProduct.data === undefined) {
            return undefined
        }

        return getProduct.data.name
    } catch (error) {

        return undefined
    }
}