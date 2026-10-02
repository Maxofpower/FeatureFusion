'use server'

import { API_ROUTES } from "@/lib/constants/routes"
import { fetcher } from "@/lib/fetcher"
import { OrderDetail, ProductDetail } from "@/models"

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

export const getOrderNumber = async (orderId: string) => {
    try {
        const getOrder = await fetcher<OrderDetail>(`${API_ROUTES.ORDERS}/${encodeURIComponent(orderId)}`)

        if (!getOrder.success || getOrder.data === undefined) {
            return undefined
        }

        return getOrder.data.orderNumber
    } catch (error) {

        return undefined
    }
}